using ExpenseManagement.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Infrastructure;

/// <summary>
/// Entry point to the database. Unlike EF's DbContext (scoped, tracks changes),
/// this class is registered as a singleton: MongoClient is thread-safe and owns the
/// connection pool, so the official recommendation is one client per application.
/// IMongoCollection&lt;T&gt; plays a role similar to DbSet&lt;T&gt;, but there is no change
/// tracking and no SaveChanges: every call goes to the server immediately.
/// </summary>
public class MongoDbContext
{
    public const string UsersCollection = "users";
    public const string CategoriesCollection = "categories";
    public const string ExpensesCollection = "expenses";

    public MongoDbContext(IOptions<MongoDbSettings> options)
        : this(options.Value.ConnectionString, options.Value.DatabaseName)
    {
    }

    public MongoDbContext(string connectionString, string databaseName)
    {
        MongoDbConventions.Register();

        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(databaseName);
    }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    // Collections are created lazily by MongoDB on the first insert (or index creation).
    public IMongoCollection<User> Users => Database.GetCollection<User>(UsersCollection);

    public IMongoCollection<Category> Categories => Database.GetCollection<Category>(CategoriesCollection);

    public IMongoCollection<Expense> Expenses => Database.GetCollection<Expense>(ExpensesCollection);
}
