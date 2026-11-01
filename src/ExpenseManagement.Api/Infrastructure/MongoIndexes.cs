using ExpenseManagement.Api.Models;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Infrastructure;

/// <summary>
/// Index definitions. MongoDB has no migrations: indexes are created from code at startup.
/// Index creation is idempotent, so running it on every start is safe (an index that
/// already exists with the same keys and options is simply left as is).
/// </summary>
public static class MongoIndexes
{
    public const string UserEmailUnique = "ux_users_email";
    public const string CategoryNameUnique = "ux_categories_name";
    public const string ExpenseUserIdDate = "ix_expenses_userId_date";
    public const string ExpenseCategoryId = "ix_expenses_categoryId";

    public static async Task CreateAsync(MongoDbContext context, CancellationToken ct = default)
    {
        // 1) Unique index on users.email
        // Same as a UNIQUE constraint in SQL Server. Makes lookups by email fast and
        // guarantees no duplicates even with concurrent inserts (the app check alone could race).
        await context.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Name = UserEmailUnique, Unique = true }),
            cancellationToken: ct);

        // Category names are unique too (keeps the seed idempotent and the data clean).
        await context.Categories.Indexes.CreateOneAsync(
            new CreateIndexModel<Category>(
                Builders<Category>.IndexKeys.Ascending(c => c.Name),
                new CreateIndexOptions { Name = CategoryNameUnique, Unique = true }),
            cancellationToken: ct);

        await context.Expenses.Indexes.CreateManyAsync(
        [
            // 2) Compound index { userId: 1, date: -1 }
            // Supports the most common query: "expenses of a user, optionally in a date range,
            // newest first". Equality field first (userId), then the range/sort field (date):
            // the ESR rule (Equality, Sort, Range). The index already returns documents in date
            // order, so no in-memory SORT stage is needed. Its prefix { userId } also serves
            // queries that only filter by user, and the aggregation $match by user.
            new CreateIndexModel<Expense>(
                Builders<Expense>.IndexKeys.Ascending(e => e.UserId).Descending(e => e.Date),
                new CreateIndexOptions { Name = ExpenseUserIdDate }),

            // 3) Index on { categoryId: 1 }
            // Supports filtering by category, grouping reports filtered by category and the
            // "is this category in use?" check before deleting it (like indexing a FK column).
            new CreateIndexModel<Expense>(
                Builders<Expense>.IndexKeys.Ascending(e => e.CategoryId),
                new CreateIndexOptions { Name = ExpenseCategoryId })
        ], ct);
    }
}
