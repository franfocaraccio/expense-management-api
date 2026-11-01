using ExpenseManagement.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Strongly typed configuration, validated when the application starts.
builder.Services.AddOptions<MongoDbSettings>()
    .Bind(builder.Configuration.GetSection(MongoDbSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Singleton: MongoClient manages its own connection pool (see MongoDbContext).
builder.Services.AddSingleton<MongoDbContext>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger is always enabled so the API can be explored in any environment.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Simple health endpoint: the "ping" command is the cheapest round trip to the server.
app.MapGet("/health", async (MongoDbContext context) =>
{
    await context.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
    return Results.Ok(new { status = "Healthy", database = context.Database.DatabaseNamespace.DatabaseName });
}).WithTags("Health");

app.Run();

// Makes Program visible to WebApplicationFactory in the test project.
public partial class Program;
