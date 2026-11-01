using System.Net;
using System.Net.Http.Json;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpenseManagement.Tests.Integration;

/// <summary>End-to-end tests through HTTP: routing, validation, status codes and the real database.</summary>
[Collection(MongoDbCollection.Name)]
public class ApiTests(MongoDbFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MongoDb:ConnectionString", fixture.ConnectionString);
            builder.UseSetting("MongoDb:DatabaseName", $"api_test_{Guid.NewGuid():N}");
            builder.UseSetting("MongoDb:SeedData", "false");
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ThenDuplicate_Returns201Then409()
    {
        var request = new CreateUserRequest("Ana", "ana@example.com");

        var created = await _client.PostAsJsonAsync("/api/users", request);
        var duplicate = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task InvalidObjectIdInRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/users/not-an-object-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvalidObjectIdInQuery_Returns400()
    {
        var response = await _client.GetAsync("/api/expenses?userId=abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidBody_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { name = "", email = "not-an-email" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
