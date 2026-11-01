using ExpenseManagement.Tests.Infrastructure;

namespace ExpenseManagement.Tests.Integration;

public class ReportAndDiagnosticsTests(MongoDbFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Reports_AggregateOnTheServer()
    {
        var ana = await CreateUserAsync("Ana");
        var bruno = await CreateUserAsync("Bruno");
        var food = await CreateCategoryAsync("Food");
        var travel = await CreateCategoryAsync("Travel");

        await CreateExpenseAsync(ana.Id, food.Id, 10.10m, new DateTime(2025, 1, 5));
        await CreateExpenseAsync(ana.Id, travel.Id, 100m, new DateTime(2025, 1, 20));
        await CreateExpenseAsync(ana.Id, food.Id, 5m, new DateTime(2025, 2, 1));
        await CreateExpenseAsync(bruno.Id, food.Id, 7.25m, new DateTime(2025, 2, 10));

        var byUser = await Reports.GetTotalsByUserAsync(null, null, Ct);
        Assert.Equal(2, byUser.Count);
        Assert.Equal("Ana", byUser[0].UserName);            // sorted by total desc
        Assert.Equal(115.10m, byUser[0].Total);              // Decimal128: no rounding errors
        Assert.Equal(3, byUser[0].Count);

        var byCategory = await Reports.GetTotalsByCategoryAsync(ana.Id, null, null, Ct);
        Assert.Equal(["Travel", "Food"], byCategory.Select(c => c.CategoryName));
        Assert.Equal(15.10m, byCategory[1].Total);

        var monthly = await Reports.GetMonthlyTotalsAsync(null, 2025, Ct);
        Assert.Equal([(1, 110.10m), (2, 12.25m)], monthly.Select(m => (m.Month, m.Total)));
    }

    [Fact]
    public async Task Explain_ByUser_UsesCompoundIndex_AndCollectionScanWithoutIt()
    {
        var user = await CreateUserAsync();
        var category = await CreateCategoryAsync();
        for (var i = 0; i < 30; i++)
        {
            await CreateExpenseAsync(user.Id, category.Id, 1m, new DateTime(2025, 1, 1).AddDays(i));
        }

        var result = await Diagnostics.ExplainExpensesByUserAsync(user.Id, null, null, includeRaw: false, Ct);

        Assert.True(result.WithIndex.UsesIndex);
        Assert.Equal("ix_expenses_userId_date", result.WithIndex.IndexName);
        Assert.False(result.WithIndex.HasInMemorySort);
        Assert.Equal(20, result.WithIndex.TotalDocsExamined);

        Assert.False(result.WithoutIndex.UsesIndex);
        Assert.Contains("COLLSCAN", result.WithoutIndex.Stages);
        Assert.Equal(30, result.WithoutIndex.TotalDocsExamined);
    }
}
