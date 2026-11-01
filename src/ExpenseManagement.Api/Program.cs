using System.Text.Json.Serialization;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
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

// Services are stateless and only hold collection references, so scoped is enough.
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<ReportService>();

builder.Services.Configure<RouteOptions>(options =>
    options.ConstraintMap.Add("objectid", typeof(ObjectIdRouteConstraint)));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Expense Management API",
        Version = "v1",
        Description = "Reference project: MongoDB 8 with the official .NET driver."
    });
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "ExpenseManagement.Api.xml"));
});

builder.Services.AddTransient<DataSeeder>();

var app = builder.Build();

// Create indexes (idempotent) and seed sample data before accepting requests.
await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    var settings = scope.ServiceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;

    await MongoIndexes.CreateAsync(context);

    if (settings.SeedData)
    {
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(settings.SeedExpenseCount);
    }
}

app.UseExceptionHandler();

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
