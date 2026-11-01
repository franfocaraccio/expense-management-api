using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Api.Models;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Services;

public class ExpenseService(MongoDbContext context, UserService userService, CategoryService categoryService)
{
    private readonly IMongoCollection<Expense> _expenses = context.Expenses;

    /// <summary>
    /// Filtering, sorting and pagination in a single query.
    /// The filter is built dynamically, similar to composing Where() calls on an IQueryable.
    /// </summary>
    public async Task<PagedResult<ExpenseDto>> SearchAsync(ExpenseQuery query, CancellationToken ct)
    {
        var filter = BuildFilter(query);
        var sort = query.Sort == SortOrder.Asc
            ? Builders<Expense>.Sort.Ascending(e => e.Date)
            : Builders<Expense>.Sort.Descending(e => e.Date);

        var total = await _expenses.CountDocumentsAsync(filter, cancellationToken: ct);
        var expenses = await _expenses.Find(filter)
            .Sort(sort)
            .Skip(query.Skip)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ExpenseDto>(
            expenses.Select(ExpenseDto.From).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<ExpenseDto> GetByIdAsync(string id, CancellationToken ct)
    {
        return ExpenseDto.From(await GetDocumentAsync(id, ct));
    }

    public async Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct)
    {
        await ValidateReferencesAsync(request.UserId, request.CategoryId, ct);

        var expense = new Expense
        {
            UserId = request.UserId,
            CategoryId = request.CategoryId,
            Amount = request.Amount,
            Description = request.Description,
            Date = Guard.ToUtc(request.Date!.Value),
            CreatedAt = DateTime.UtcNow
        };

        await _expenses.InsertOneAsync(expense, cancellationToken: ct);
        return ExpenseDto.From(expense);
    }

    /// <summary>
    /// Full replacement (PUT). ReplaceOneAsync needs the complete document, so we read the
    /// current one first to keep createdAt, which the client does not send.
    /// </summary>
    public async Task<ExpenseDto> ReplaceAsync(string id, SaveExpenseRequest request, CancellationToken ct)
    {
        var existing = await GetDocumentAsync(id, ct);
        await ValidateReferencesAsync(request.UserId, request.CategoryId, ct);

        var replacement = new Expense
        {
            Id = id,
            UserId = request.UserId,
            CategoryId = request.CategoryId,
            Amount = request.Amount,
            Description = request.Description,
            Date = Guard.ToUtc(request.Date!.Value),
            CreatedAt = existing.CreatedAt
        };

        var result = await _expenses.ReplaceOneAsync(e => e.Id == id, replacement, cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            throw new NotFoundException($"Expense '{id}' was not found.");
        }

        return ExpenseDto.From(replacement);
    }

    /// <summary>
    /// Partial update (PATCH). Builds a $set only with the fields that were sent and uses
    /// FindOneAndUpdateAsync, which updates and returns the document in one atomic operation.
    /// </summary>
    public async Task<ExpenseDto> PatchAsync(string id, PatchExpenseRequest request, CancellationToken ct)
    {
        var updates = new List<UpdateDefinition<Expense>>();

        if (request.CategoryId is not null)
        {
            Guard.ValidObjectId(request.CategoryId, nameof(request.CategoryId));
            await categoryService.EnsureExistsAsync(request.CategoryId, ct);
            updates.Add(Builders<Expense>.Update.Set(e => e.CategoryId, request.CategoryId));
        }

        if (request.Amount.HasValue)
            updates.Add(Builders<Expense>.Update.Set(e => e.Amount, request.Amount.Value));

        if (request.Description is not null)
            updates.Add(Builders<Expense>.Update.Set(e => e.Description, request.Description));

        if (request.Date.HasValue)
            updates.Add(Builders<Expense>.Update.Set(e => e.Date, Guard.ToUtc(request.Date.Value)));

        if (updates.Count == 0)
        {
            throw new BadRequestException("At least one field must be provided.");
        }

        var updated = await _expenses.FindOneAndUpdateAsync(
            Builders<Expense>.Filter.Eq(e => e.Id, id),
            Builders<Expense>.Update.Combine(updates),
            new FindOneAndUpdateOptions<Expense> { ReturnDocument = ReturnDocument.After },
            ct);

        return updated is null
            ? throw new NotFoundException($"Expense '{id}' was not found.")
            : ExpenseDto.From(updated);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        var result = await _expenses.DeleteOneAsync(e => e.Id == id, ct);
        if (result.DeletedCount == 0)
        {
            throw new NotFoundException($"Expense '{id}' was not found.");
        }
    }

    /// <summary>Translates the query string into a MongoDB filter document.</summary>
    internal static FilterDefinition<Expense> BuildFilter(ExpenseQuery query)
    {
        var builder = Builders<Expense>.Filter;
        var filters = new List<FilterDefinition<Expense>>();

        if (query.UserId is not null)
        {
            Guard.ValidObjectId(query.UserId, nameof(query.UserId));
            filters.Add(builder.Eq(e => e.UserId, query.UserId));            // { userId: ObjectId(...) }
        }

        if (query.CategoryId is not null)
        {
            Guard.ValidObjectId(query.CategoryId, nameof(query.CategoryId));
            filters.Add(builder.Eq(e => e.CategoryId, query.CategoryId));    // { categoryId: ObjectId(...) }
        }

        Guard.ValidDateRange(query.From, query.To);
        if (query.From.HasValue)
            filters.Add(builder.Gte(e => e.Date, Guard.ToUtc(query.From.Value)));  // { date: { $gte: ... } }
        if (query.To.HasValue)
            filters.Add(builder.Lt(e => e.Date, Guard.ToUtc(query.To.Value)));     // { date: { $lt: ... } }

        // Several conditions are combined with $and (the driver merges them into one document).
        return filters.Count == 0 ? builder.Empty : builder.And(filters);
    }

    private async Task<Expense> GetDocumentAsync(string id, CancellationToken ct)
    {
        return await _expenses.Find(e => e.Id == id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException($"Expense '{id}' was not found.");
    }

    /// <summary>
    /// The application plays the role of the FOREIGN KEY constraint: MongoDB would happily
    /// store an expense pointing to a user that does not exist.
    /// </summary>
    private async Task ValidateReferencesAsync(string userId, string categoryId, CancellationToken ct)
    {
        Guard.ValidObjectId(userId, "userId");
        Guard.ValidObjectId(categoryId, "categoryId");

        await Task.WhenAll(
            userService.EnsureExistsAsync(userId, ct),
            categoryService.EnsureExistsAsync(categoryId, ct));
    }
}
