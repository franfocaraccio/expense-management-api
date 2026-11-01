using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Models;
using ExpenseManagement.Tests.Infrastructure;
using MongoDB.Driver;

namespace ExpenseManagement.Tests.Integration;

public class UserServiceTests(MongoDbFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_GeneratesObjectId_AndStoresEmailLowercased()
    {
        var user = await Users.CreateAsync(new CreateUserRequest("Ana", "Ana.Perez@Example.COM"), Ct);

        Assert.Matches("^[0-9a-f]{24}$", user.Id);
        Assert.Equal("ana.perez@example.com", user.Email);
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_ThrowsConflict_BecauseOfUniqueIndex()
    {
        await Users.CreateAsync(new CreateUserRequest("Ana", "ana@example.com"), Ct);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Users.CreateAsync(new CreateUserRequest("Other", "ANA@example.com"), Ct));
    }

    [Fact]
    public async Task Update_ChangesNameAndEmail_AndKeepsCreatedAt()
    {
        var user = await CreateUserAsync();

        var updated = await Users.UpdateAsync(user.Id, new UpdateUserRequest("Ana Maria", "new@example.com"), Ct);

        Assert.Equal("Ana Maria", updated.Name);
        Assert.Equal("new@example.com", updated.Email);
        Assert.Equal(user.CreatedAt, updated.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Delete_RemovesUserAndAllExpenses_InOneTransaction()
    {
        var user = await CreateUserAsync("Ana");
        var other = await CreateUserAsync("Bruno");
        var category = await CreateCategoryAsync();
        await CreateExpenseAsync(user.Id, category.Id, 10m, new DateTime(2025, 1, 1));
        await CreateExpenseAsync(user.Id, category.Id, 20m, new DateTime(2025, 1, 2));
        await CreateExpenseAsync(other.Id, category.Id, 30m, new DateTime(2025, 1, 3));

        var result = await Users.DeleteAsync(user.Id, Ct);

        Assert.Equal(2, result.DeletedExpenses);
        await Assert.ThrowsAsync<NotFoundException>(() => Users.GetByIdAsync(user.Id, Ct));
        Assert.Equal(1, await Context.Expenses.CountDocumentsAsync(FilterDefinition<Expense>.Empty));
    }

    [Fact]
    public async Task Delete_UnknownUser_ThrowsNotFound_AndAbortsTheTransaction()
    {
        var category = await CreateCategoryAsync();
        var user = await CreateUserAsync();
        await CreateExpenseAsync(user.Id, category.Id, 10m, new DateTime(2025, 1, 1));

        await Assert.ThrowsAsync<NotFoundException>(() => Users.DeleteAsync("000000000000000000000000", Ct));

        Assert.Equal(1, await Context.Expenses.CountDocumentsAsync(FilterDefinition<Expense>.Empty));
    }
}
