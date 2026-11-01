using ExpenseManagement.Api.Services;
using MongoDB.Bson;

namespace ExpenseManagement.Tests.Unit;

/// <summary>Pure unit tests (no Docker): the explain output shapes the parser must understand.</summary>
public class ExplainParserTests
{
    private static BsonDocument Stats(long returned, long keys, long docs) => new()
    {
        { "nReturned", returned },
        { "totalKeysExamined", keys },
        { "totalDocsExamined", docs },
        { "executionTimeMillis", 1 }
    };

    [Fact]
    public void Summarize_ClassicEngine_IndexScan()
    {
        var explain = BsonDocument.Parse("""
            {
              queryPlanner: { winningPlan: {
                stage: "LIMIT",
                inputStage: { stage: "FETCH", inputStage: { stage: "IXSCAN", indexName: "ix_expenses_userId_date" } }
              } }
            }
            """);
        explain.Add("executionStats", Stats(20, 20, 20));

        var summary = ExplainParser.Summarize(explain);

        Assert.Equal("LIMIT > FETCH > IXSCAN", summary.Stages);
        Assert.Equal("ix_expenses_userId_date", summary.IndexName);
        Assert.True(summary.UsesIndex);
        Assert.False(summary.HasInMemorySort);
        Assert.Null(summary.Raw);
    }

    [Fact]
    public void Summarize_SlotBasedEngine_ReadsNestedQueryPlan()
    {
        var explain = BsonDocument.Parse("""
            {
              queryPlanner: { winningPlan: {
                queryPlan: { stage: "SORT", inputStage: { stage: "COLLSCAN" } },
                slotBasedPlan: { stages: "..." }
              } }
            }
            """);
        explain.Add("executionStats", Stats(20, 0, 50_000));

        var summary = ExplainParser.Summarize(explain, includeRaw: true);

        Assert.Equal("SORT > COLLSCAN", summary.Stages);
        Assert.False(summary.UsesIndex);
        Assert.True(summary.HasInMemorySort);
        Assert.Equal(50_000, summary.TotalDocsExamined);
        Assert.NotNull(summary.Raw);
    }
}
