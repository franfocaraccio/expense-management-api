using System.Text.Json;
using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Services;

/// <summary>
/// Runs explain() to show how MongoDB executes a query: which plan won, whether it used
/// an index (IXSCAN) or read the whole collection (COLLSCAN), and how many index keys and
/// documents it had to examine. This is the equivalent of the Actual Execution Plan in
/// SQL Server Management Studio.
/// </summary>
public class DiagnosticsService(MongoDbContext context)
{
    private const int Limit = 20;

    /// <summary>
    /// Query served by the compound index { userId: 1, date: -1 }. With the index the server
    /// reads ~20 keys and ~20 documents; with a collection scan it reads every document
    /// and must sort the matches in memory.
    /// </summary>
    public Task<ExplainComparisonDto> ExplainExpensesByUserAsync(
        string userId, DateTime? from, DateTime? to, bool includeRaw, CancellationToken ct)
    {
        Guard.ValidObjectId(userId, nameof(userId));
        Guard.ValidDateRange(from, to);

        var filter = new BsonDocument("userId", ObjectId.Parse(userId));
        if (from.HasValue || to.HasValue)
        {
            var range = new BsonDocument();
            if (from.HasValue) range.Add("$gte", Guard.ToUtc(from.Value));
            if (to.HasValue) range.Add("$lt", Guard.ToUtc(to.Value));
            filter.Add("date", range);
        }

        return CompareAsync(filter, new BsonDocument("date", -1), includeRaw, ct);
    }

    /// <summary>
    /// Query served by the single-field index { categoryId: 1 }. The index finds the
    /// documents, but it does not contain "date", so the results are sorted in memory
    /// (a SORT stage). A compound index { categoryId: 1, date: -1 } would remove that stage.
    /// </summary>
    public Task<ExplainComparisonDto> ExplainExpensesByCategoryAsync(string categoryId, bool includeRaw, CancellationToken ct)
    {
        Guard.ValidObjectId(categoryId, nameof(categoryId));
        var filter = new BsonDocument("categoryId", ObjectId.Parse(categoryId));
        return CompareAsync(filter, new BsonDocument("date", -1), includeRaw, ct);
    }

    /// <summary>Indexes of every collection plus their usage counters ($indexStats).</summary>
    public async Task<IReadOnlyList<IndexInfoDto>> GetIndexesAsync(CancellationToken ct)
    {
        var result = new List<IndexInfoDto>();

        foreach (var name in new[] { MongoDbContext.UsersCollection, MongoDbContext.CategoriesCollection, MongoDbContext.ExpensesCollection })
        {
            var collection = context.Database.GetCollection<BsonDocument>(name);

            // $indexStats: how many operations used each index since the server started.
            var stats = await collection.Aggregate()
                .AppendStage<BsonDocument>(new BsonDocument("$indexStats", new BsonDocument()))
                .ToListAsync(ct);
            var usage = stats.ToDictionary(s => s["name"].AsString, s => s["accesses"]["ops"].ToInt64());

            using var cursor = await collection.Indexes.ListAsync(ct);
            foreach (var index in await cursor.ToListAsync(ct))
            {
                var indexName = index["name"].AsString;
                result.Add(new IndexInfoDto(
                    name,
                    indexName,
                    ToJson(index["key"].AsBsonDocument),
                    // The _id index is always unique, although listIndexes does not report the flag.
                    indexName == "_id_" || index.GetValue("unique", false).ToBoolean(),
                    usage.TryGetValue(indexName, out var ops) ? ops : null));
            }
        }

        return result;
    }

    private async Task<ExplainComparisonDto> CompareAsync(
        BsonDocument filter, BsonDocument sort, bool includeRaw, CancellationToken ct)
    {
        // No hint: the query planner chooses the best plan (it should pick our index).
        var withIndex = await ExplainFindAsync(filter, sort, hint: null, ct);

        // hint { $natural: 1 } forces a collection scan, as if the index did not exist.
        var withoutIndex = await ExplainFindAsync(filter, sort, new BsonDocument("$natural", 1), ct);

        var shell = new JsonWriterSettings { OutputMode = JsonOutputMode.Shell };
        var query = $"db.{MongoDbContext.ExpensesCollection}.find({filter.ToJson(shell)})" +
                    $".sort({sort.ToJson(shell)}).limit({Limit}).explain(\"executionStats\")";

        return new ExplainComparisonDto(
            query,
            ExplainParser.Summarize(withIndex, includeRaw),
            ExplainParser.Summarize(withoutIndex, includeRaw));
    }

    /// <summary>
    /// The driver has no Explain() method on Find, so we send the "explain" command directly:
    /// { explain: { find: "expenses", filter: ..., sort: ..., limit: 20 }, verbosity: "executionStats" }
    /// "executionStats" actually runs the query and reports real counters and timings.
    /// </summary>
    private Task<BsonDocument> ExplainFindAsync(BsonDocument filter, BsonDocument sort, BsonDocument? hint, CancellationToken ct)
    {
        var find = new BsonDocument
        {
            { "find", MongoDbContext.ExpensesCollection },
            { "filter", filter },
            { "sort", sort },
            { "limit", Limit }
        };
        if (hint is not null)
        {
            find.Add("hint", hint);
        }

        var command = new BsonDocument
        {
            { "explain", find },
            { "verbosity", "executionStats" }
        };

        return context.Database.RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
    }

    internal static JsonElement ToJson(BsonDocument document)
    {
        // Relaxed Extended JSON: ObjectId becomes { "$oid": "..." }, numbers stay numbers.
        var json = document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

/// <summary>Extracts a readable summary from the explain output.</summary>
public static class ExplainParser
{
    public static ExplainSummaryDto Summarize(BsonDocument explain, bool includeRaw = false)
    {
        var winningPlan = explain["queryPlanner"]["winningPlan"].AsBsonDocument;

        // With the Slot-Based Execution engine (default for many queries since 7.0/8.0) the
        // classic plan tree is nested under "queryPlan"; with the classic engine it is the root.
        var plan = winningPlan.TryGetValue("queryPlan", out var queryPlan) ? queryPlan.AsBsonDocument : winningPlan;

        var stages = new List<string>();
        string? indexName = null;

        for (var stage = plan; stage is not null; stage = NextStage(stage))
        {
            var name = stage.GetValue("stage", "?").AsString;
            stages.Add(name);
            if (name == "IXSCAN" && stage.TryGetValue("indexName", out var index))
            {
                indexName = index.AsString;
            }
        }

        var stats = explain["executionStats"].AsBsonDocument;

        return new ExplainSummaryDto(
            string.Join(" > ", stages),
            indexName,
            UsesIndex: stages.Contains("IXSCAN"),
            HasInMemorySort: stages.Contains("SORT"),
            stats["nReturned"].ToInt64(),
            stats["totalKeysExamined"].ToInt64(),
            stats["totalDocsExamined"].ToInt64(),
            stats["executionTimeMillis"].ToInt64(),
            includeRaw ? DiagnosticsService.ToJson(explain) : null);
    }

    private static BsonDocument? NextStage(BsonDocument stage)
    {
        if (stage.TryGetValue("inputStage", out var input))
        {
            return input.AsBsonDocument;
        }

        // Stages such as OR or SORT_MERGE have several children; the first one is enough here.
        if (stage.TryGetValue("inputStages", out var inputs) && inputs.AsBsonArray.Count > 0)
        {
            return inputs.AsBsonArray[0].AsBsonDocument;
        }

        return null;
    }
}
