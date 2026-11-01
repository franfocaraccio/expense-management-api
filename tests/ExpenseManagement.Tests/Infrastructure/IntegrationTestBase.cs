using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Api.Services;

namespace ExpenseManagement.Tests.Infrastructure;

/// <summary>Creates a fresh database and the services under test for each test class instance.</summary>
[Collection(MongoDbCollection.Name)]
public abstract class IntegrationTestBase(MongoDbFixture fixture) : IAsyncLifetime
{
    protected MongoDbContext Context { get; private set; } = null!;
    protected UserService Users { get; private set; } = null!;
    protected CategoryService Categories { get; private set; } = null!;
    protected ExpenseService Expenses { get; private set; } = null!;
    protected ReportService Reports { get; private set; } = null!;
    protected DiagnosticsService Diagnostics { get; private set; } = null!;

    protected static CancellationToken Ct => CancellationToken.None;

    public async Task InitializeAsync()
    {
        Context = await fixture.CreateContextAsync();
        Users = new UserService(Context);
        Categories = new CategoryService(Context);
        Expenses = new ExpenseService(Context, Users, Categories);
        Reports = new ReportService(Context);
        Diagnostics = new DiagnosticsService(Context);
    }

    public Task DisposeAsync() => Context.Client.DropDatabaseAsync(Context.Database.DatabaseNamespace.DatabaseName);

    protected Task<UserDto> CreateUserAsync(string name = "Ana") =>
        Users.CreateAsync(new CreateUserRequest(name, $"{name}.{Guid.NewGuid():N}@example.com"), Ct);

    protected Task<CategoryDto> CreateCategoryAsync(string name = "Food") =>
        Categories.CreateAsync(new SaveCategoryRequest(name, null), Ct);

    protected Task<ExpenseDto> CreateExpenseAsync(string userId, string categoryId, decimal amount, DateTime date) =>
        Expenses.CreateAsync(new SaveExpenseRequest(userId, categoryId, amount, "test", date), Ct);
}
