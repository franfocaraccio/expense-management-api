using ExpenseManagement.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Infrastructure;

/// <summary>
/// Generates sample data so that queries, aggregations and explain() have something
/// meaningful to work with. Runs only when the users collection is empty.
/// </summary>
public class DataSeeder(MongoDbContext context, ILogger<DataSeeder> logger)
{
    private const int UserCount = 20;
    private const int BatchSize = 5_000;

    private static readonly (string Name, string Description, decimal Min, decimal Max)[] CategoryData =
    [
        ("Food", "Groceries and restaurants", 5m, 120m),
        ("Transport", "Fuel, public transport and taxis", 2m, 80m),
        ("Housing", "Rent, maintenance and repairs", 300m, 1500m),
        ("Utilities", "Electricity, water, gas and internet", 20m, 200m),
        ("Health", "Pharmacy and medical appointments", 10m, 400m),
        ("Entertainment", "Movies, concerts and streaming", 5m, 150m),
        ("Education", "Courses and books", 15m, 500m),
        ("Travel", "Flights and hotels", 100m, 3000m)
    ];

    private static readonly string[] FirstNames =
        ["Ana", "Bruno", "Carla", "Diego", "Elena", "Facundo", "Gabriela", "Hernan", "Ines", "Juan"];

    private static readonly string[] LastNames = ["Garcia", "Lopez", "Martinez", "Perez", "Rodriguez"];

    public async Task SeedAsync(int expenseCount, CancellationToken ct = default)
    {
        if (await context.Users.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Seed skipped: the database already contains data");
            return;
        }

        // Fixed seed: every fresh database gets the same data, which makes examples reproducible.
        var random = new Random(42);
        var now = DateTime.UtcNow;

        var categories = CategoryData
            .Select(c => new Category { Id = ObjectId.GenerateNewId().ToString(), Name = c.Name, Description = c.Description })
            .ToList();
        await context.Categories.InsertManyAsync(categories, cancellationToken: ct);

        var users = Enumerable.Range(1, UserCount)
            .Select(i =>
            {
                var first = FirstNames[(i - 1) % FirstNames.Length];
                var last = LastNames[(i - 1) / FirstNames.Length % LastNames.Length];
                return new User
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Name = $"{first} {last}",
                    Email = $"{first}.{last}{i}@example.com".ToLowerInvariant(),
                    CreatedAt = now.AddDays(-random.Next(730, 900))
                };
            })
            .ToList();
        await context.Users.InsertManyAsync(users, cancellationToken: ct);

        // InsertManyAsync sends documents in bulk (one round trip per batch), similar to SqlBulkCopy.
        // IsOrdered = false lets the server continue with the rest of the batch if one insert fails.
        var batch = new List<Expense>(BatchSize);
        for (var i = 0; i < expenseCount; i++)
        {
            var categoryIndex = random.Next(categories.Count);
            var (name, _, min, max) = CategoryData[categoryIndex];
            var date = now.Date.AddDays(-random.Next(0, 730)).AddMinutes(random.Next(0, 24 * 60));

            batch.Add(new Expense
            {
                UserId = users[random.Next(users.Count)].Id,
                CategoryId = categories[categoryIndex].Id,
                Amount = Math.Round(min + (decimal)random.NextDouble() * (max - min), 2),
                Description = $"{name} expense #{i + 1}",
                Date = date,
                CreatedAt = date
            });

            if (batch.Count == BatchSize || i == expenseCount - 1)
            {
                await context.Expenses.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
                batch.Clear();
            }
        }

        logger.LogInformation(
            "Seed completed: {Categories} categories, {Users} users, {Expenses} expenses",
            categories.Count, users.Count, expenseCount);
    }
}
