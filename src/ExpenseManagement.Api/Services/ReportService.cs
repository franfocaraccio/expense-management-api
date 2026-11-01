using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Services;

/// <summary>
/// Reports built with aggregation pipelines. A pipeline is a list of stages; each stage
/// receives the documents produced by the previous one ($match ~ WHERE, $group ~ GROUP BY,
/// $sort ~ ORDER BY, $lookup ~ LEFT JOIN, $project ~ SELECT list).
/// All the work happens on the server: only the final, small result travels to the API.
///
/// The stages are written as BsonDocument so they read exactly like the mongosh syntax
/// and can be copied into MongoDB Compass (Aggregations tab) for ad-hoc analysis.
/// </summary>
public class ReportService(MongoDbContext context)
{
    private readonly IMongoCollection<Expense> _expenses = context.Expenses;

    /// <summary>
    /// SQL equivalent:
    /// SELECT e.UserId, u.Name, SUM(e.Amount), COUNT(*), AVG(e.Amount)
    /// FROM Expenses e LEFT JOIN Users u ON u.Id = e.UserId
    /// WHERE e.Date >= @from AND e.Date &lt; @to
    /// GROUP BY e.UserId, u.Name ORDER BY SUM(e.Amount) DESC
    /// </summary>
    public async Task<IReadOnlyList<UserTotalDto>> GetTotalsByUserAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var pipeline = new List<BsonDocument>();
        AddMatchStage(pipeline, userId: null, categoryId: null, from, to);

        pipeline.AddRange(
        [
            // Group first, join later: $lookup runs once per user (20 times), not once per expense.
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$userId" },
                { "total", new BsonDocument("$sum", "$amount") },
                { "count", new BsonDocument("$sum", 1) },
                { "average", new BsonDocument("$avg", "$amount") }
            }),
            new BsonDocument("$sort", new BsonDocument("total", -1)),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", MongoDbContext.UsersCollection },
                { "localField", "_id" },
                { "foreignField", "_id" },
                { "as", "user" }
            }),
            // $lookup always returns an array; $unwind turns it into a single embedded object.
            // preserveNullAndEmptyArrays keeps totals of users that were deleted (LEFT JOIN).
            new BsonDocument("$unwind", new BsonDocument
            {
                { "path", "$user" },
                { "preserveNullAndEmptyArrays", true }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "_id", 0 },
                { "userId", "$_id" },
                { "userName", "$user.name" },
                { "email", "$user.email" },
                { "total", 1 },
                { "count", 1 },
                { "average", new BsonDocument("$round", new BsonArray { "$average", 2 }) }
            })
        ]);

        return await RunAsync<UserTotalDto>(pipeline, ct);
    }

    /// <summary>Totals grouped by category, optionally for one user and/or a date range.</summary>
    public async Task<IReadOnlyList<CategoryTotalDto>> GetTotalsByCategoryAsync(
        string? userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var pipeline = new List<BsonDocument>();
        AddMatchStage(pipeline, userId, categoryId: null, from, to);

        pipeline.AddRange(
        [
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$categoryId" },
                { "total", new BsonDocument("$sum", "$amount") },
                { "count", new BsonDocument("$sum", 1) },
                { "average", new BsonDocument("$avg", "$amount") }
            }),
            new BsonDocument("$sort", new BsonDocument("total", -1)),
            // $lookup with a sub-pipeline: only bring the "name" field from categories.
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", MongoDbContext.CategoriesCollection },
                { "localField", "_id" },
                { "foreignField", "_id" },
                { "pipeline", new BsonArray { new BsonDocument("$project", new BsonDocument("name", 1)) } },
                { "as", "category" }
            }),
            new BsonDocument("$unwind", new BsonDocument
            {
                { "path", "$category" },
                { "preserveNullAndEmptyArrays", true }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "_id", 0 },
                { "categoryId", "$_id" },
                { "categoryName", "$category.name" },
                { "total", 1 },
                { "count", 1 },
                { "average", new BsonDocument("$round", new BsonArray { "$average", 2 }) }
            })
        ]);

        return await RunAsync<CategoryTotalDto>(pipeline, ct);
    }

    /// <summary>
    /// Monthly totals. Grouping by a compound _id { year, month } is the equivalent of
    /// GROUP BY YEAR(Date), MONTH(Date) in SQL Server.
    /// </summary>
    public async Task<IReadOnlyList<MonthlyTotalDto>> GetMonthlyTotalsAsync(string? userId, int? year, CancellationToken ct)
    {
        DateTime? from = year.HasValue ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null;
        DateTime? to = from?.AddYears(1);

        var pipeline = new List<BsonDocument>();
        AddMatchStage(pipeline, userId, categoryId: null, from, to);

        pipeline.AddRange(
        [
            new BsonDocument("$group", new BsonDocument
            {
                {
                    "_id", new BsonDocument
                    {
                        { "year", new BsonDocument("$year", "$date") },
                        { "month", new BsonDocument("$month", "$date") }
                    }
                },
                { "total", new BsonDocument("$sum", "$amount") },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument { { "_id.year", 1 }, { "_id.month", 1 } }),
            new BsonDocument("$project", new BsonDocument
            {
                { "_id", 0 },
                { "year", "$_id.year" },
                { "month", "$_id.month" },
                { "total", 1 },
                { "count", 1 }
            })
        ]);

        return await RunAsync<MonthlyTotalDto>(pipeline, ct);
    }

    private async Task<IReadOnlyList<T>> RunAsync<T>(List<BsonDocument> pipeline, CancellationToken ct)
    {
        // The stages are untyped BSON; T only describes the shape of the output documents.
        PipelineDefinition<Expense, T> definition = pipeline.ToArray();
        using var cursor = await _expenses.AggregateAsync(definition, cancellationToken: ct);
        return await cursor.ToListAsync(ct);
    }

    /// <summary>
    /// $match goes first so the pipeline can use indexes and every later stage processes
    /// fewer documents (same idea as filtering before grouping in SQL).
    /// In raw BSON, references must be real ObjectIds: a string "65f..." would not match.
    /// </summary>
    private static void AddMatchStage(List<BsonDocument> pipeline, string? userId, string? categoryId, DateTime? from, DateTime? to)
    {
        var match = new BsonDocument();

        if (userId is not null)
        {
            Guard.ValidObjectId(userId, nameof(userId));
            match.Add("userId", ObjectId.Parse(userId));
        }

        if (categoryId is not null)
        {
            Guard.ValidObjectId(categoryId, nameof(categoryId));
            match.Add("categoryId", ObjectId.Parse(categoryId));
        }

        Guard.ValidDateRange(from, to);
        if (from.HasValue || to.HasValue)
        {
            var range = new BsonDocument();
            if (from.HasValue) range.Add("$gte", Guard.ToUtc(from.Value));
            if (to.HasValue) range.Add("$lt", Guard.ToUtc(to.Value));
            match.Add("date", range);
        }

        if (match.ElementCount > 0)
        {
            pipeline.Add(new BsonDocument("$match", match));
        }
    }
}
