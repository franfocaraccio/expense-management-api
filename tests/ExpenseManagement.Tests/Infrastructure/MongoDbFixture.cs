using ExpenseManagement.Api.Infrastructure;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace ExpenseManagement.Tests.Infrastructure;

/// <summary>
/// Starts one real MongoDB 8 container for all integration tests (Docker required).
/// It runs as a single-node replica set because transactions do not work on a standalone server.
/// Each test class gets its own database, so tests do not see each other's data.
/// </summary>
public class MongoDbFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
        .WithReplicaSet("rs0")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Talk to the mapped port directly instead of using replica set discovery,
        // which would try to reach the member by its container-internal host name.
        ConnectionString = new MongoUrlBuilder(_container.GetConnectionString()) { DirectConnection = true }
            .ToMongoUrl()
            .ToString();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<MongoDbContext> CreateContextAsync()
    {
        var context = new MongoDbContext(ConnectionString, $"test_{Guid.NewGuid():N}");
        await MongoIndexes.CreateAsync(context);
        return context;
    }
}

[CollectionDefinition(Name)]
public class MongoDbCollection : ICollectionFixture<MongoDbFixture>
{
    public const string Name = "MongoDB";
}
