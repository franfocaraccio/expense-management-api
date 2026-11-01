using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Api.Models;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace ExpenseManagement.Api.Services;

public class CategoryService(MongoDbContext context)
{
    private readonly IMongoCollection<Category> _categories = context.Categories;
    private readonly IMongoCollection<Expense> _expenses = context.Expenses;

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct)
    {
        // The driver also supports LINQ. This is translated to a find/aggregate command
        // on the server, the same idea as LINQ to Entities generating SQL.
        var categories = await _categories.AsQueryable()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return categories.Select(CategoryDto.From).ToList();
    }

    public async Task<CategoryDto> GetByIdAsync(string id, CancellationToken ct)
    {
        var category = await _categories.Find(c => c.Id == id).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException($"Category '{id}' was not found.");
        return CategoryDto.From(category);
    }

    public async Task<CategoryDto> CreateAsync(SaveCategoryRequest request, CancellationToken ct)
    {
        var category = new Category { Name = request.Name.Trim(), Description = request.Description };

        try
        {
            await _categories.InsertOneAsync(category, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"A category named '{category.Name}' already exists.");
        }

        return CategoryDto.From(category);
    }

    /// <summary>
    /// ReplaceOneAsync swaps the whole document (except _id). Any field missing in the
    /// replacement disappears from the stored document. Compare with UpdateOneAsync + $set
    /// in UserService, which only touches the listed fields.
    /// </summary>
    public async Task<CategoryDto> ReplaceAsync(string id, SaveCategoryRequest request, CancellationToken ct)
    {
        var replacement = new Category { Id = id, Name = request.Name.Trim(), Description = request.Description };

        ReplaceOneResult result;
        try
        {
            result = await _categories.ReplaceOneAsync(c => c.Id == id, replacement, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"A category named '{replacement.Name}' already exists.");
        }

        if (result.MatchedCount == 0)
        {
            throw new NotFoundException($"Category '{id}' was not found.");
        }

        return CategoryDto.From(replacement);
    }

    /// <summary>
    /// MongoDB has no foreign keys, so nothing stops us from deleting a category that is
    /// still referenced by expenses. The application has to enforce that rule itself.
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        // Uses the index on expenses.categoryId, so this check is cheap.
        var inUse = await _expenses.CountDocumentsAsync(
            Builders<Expense>.Filter.Eq(e => e.CategoryId, id), new CountOptions { Limit = 1 }, ct);

        if (inUse > 0)
        {
            throw new ConflictException($"Category '{id}' is used by existing expenses and cannot be deleted.");
        }

        var result = await _categories.DeleteOneAsync(c => c.Id == id, ct);
        if (result.DeletedCount == 0)
        {
            throw new NotFoundException($"Category '{id}' was not found.");
        }
    }

    public async Task EnsureExistsAsync(string id, CancellationToken ct)
    {
        var count = await _categories.CountDocumentsAsync(
            Builders<Category>.Filter.Eq(c => c.Id, id), new CountOptions { Limit = 1 }, ct);

        if (count == 0)
        {
            throw new NotFoundException($"Category '{id}' was not found.");
        }
    }
}
