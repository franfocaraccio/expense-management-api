using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Tests.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ExpenseManagement.Tests.Integration;

public class ExpenseServiceTests(MongoDbFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_WithUnknownUser_ThrowsNotFound_BecauseThereAreNoForeignKeys()
    {
        var category = await CreateCategoryAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateExpenseAsync("000000000000000000000000", category.Id, 10m, DateTime.UtcNow));
    }

    [Fact]
    public async Task Create_StoresReferencesAsObjectId_AndAmountAsDecimal128()
    {
        var user = await CreateUserAsync();
        var category = await CreateCategoryAsync();
        var expense = await CreateExpenseAsync(user.Id, category.Id, 12.34m, new DateTime(2025, 3, 10));

        // Read the raw BSON document to see the stored types.
        var raw = await Context.Database.GetCollection<BsonDocument>(MongoDbContext.ExpensesCollection)
            .Find(new BsonDocument("_id", ObjectId.Parse(expense.Id)))
            .SingleAsync();

        Assert.Equal(BsonType.ObjectId, raw["userId"].BsonType);
        Assert.Equal(BsonType.ObjectId, raw["categoryId"].BsonType);
        Assert.Equal(BsonType.Decimal128, raw["amount"].BsonType);
        Assert.Equal(new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc), raw["date"].ToUniversalTime());
    }

    [Fact]
    public async Task Search_FiltersByUserAndDateRange_SortsAndPaginates()
    {
        var user = await CreateUserAsync("Ana");
        var other = await CreateUserAsync("Bruno");
        var category = await CreateCategoryAsync();
        for (var day = 1; day <= 5; day++)
        {
            await CreateExpenseAsync(user.Id, category.Id, day, new DateTime(2025, 1, day));
        }
        await CreateExpenseAsync(user.Id, category.Id, 99m, new DateTime(2025, 2, 1));
        await CreateExpenseAsync(other.Id, category.Id, 50m, new DateTime(2025, 1, 3));

        var page = await Expenses.SearchAsync(new ExpenseQuery
        {
            UserId = user.Id,
            From = new DateTime(2025, 1, 1),
            To = new DateTime(2025, 2, 1),
            Sort = SortOrder.Asc,
            Page = 2,
            PageSize = 2
        }, Ct);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal([3m, 4m], page.Items.Select(e => e.Amount));
    }

    [Fact]
    public async Task Search_ByCategory_ReturnsNewestFirst()
    {
        var user = await CreateUserAsync();
        var food = await CreateCategoryAsync("Food");
        var travel = await CreateCategoryAsync("Travel");
        await CreateExpenseAsync(user.Id, food.Id, 1m, new DateTime(2025, 1, 1));
        await CreateExpenseAsync(user.Id, food.Id, 2m, new DateTime(2025, 1, 2));
        await CreateExpenseAsync(user.Id, travel.Id, 3m, new DateTime(2025, 1, 3));

        var page = await Expenses.SearchAsync(new ExpenseQuery { CategoryId = food.Id }, Ct);

        Assert.Equal([2m, 1m], page.Items.Select(e => e.Amount));
    }

    [Fact]
    public async Task Patch_UpdatesOnlyProvidedFields()
    {
        var user = await CreateUserAsync();
        var category = await CreateCategoryAsync();
        var expense = await CreateExpenseAsync(user.Id, category.Id, 10m, new DateTime(2025, 1, 1));

        var patched = await Expenses.PatchAsync(expense.Id, new PatchExpenseRequest(null, 25.5m, null, null), Ct);

        Assert.Equal(25.5m, patched.Amount);
        Assert.Equal(expense.Description, patched.Description);
        Assert.Equal(expense.Date, patched.Date);
    }

    [Fact]
    public async Task SearchWithDetails_ResolvesUserAndCategoryWithLookup()
    {
        var user = await CreateUserAsync("Ana");
        var category = await CreateCategoryAsync("Food");
        var expense = await CreateExpenseAsync(user.Id, category.Id, 10m, new DateTime(2025, 1, 1));

        var details = await Expenses.GetWithDetailsAsync(expense.Id, Ct);

        Assert.Equal(user.Id, details.User!.Id);
        Assert.Equal("Ana", details.User.Name);
        Assert.Equal("Food", details.Category!.Name);
    }

    [Fact]
    public async Task DeleteCategory_InUse_ThrowsConflict()
    {
        var user = await CreateUserAsync();
        var category = await CreateCategoryAsync();
        await CreateExpenseAsync(user.Id, category.Id, 10m, new DateTime(2025, 1, 1));

        await Assert.ThrowsAsync<ConflictException>(() => Categories.DeleteAsync(category.Id, Ct));
    }
}
