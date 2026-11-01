using ExpenseManagement.Api.Common;
using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Infrastructure;
using ExpenseManagement.Api.Models;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Services;

/// <summary>
/// Services talk to IMongoCollection&lt;T&gt; directly. The collection already behaves like a
/// repository (similar to DbSet&lt;T&gt;), so a generic repository on top would only hide the
/// MongoDB features this project wants to show (filters, updates, sessions, pipelines).
/// </summary>
public class UserService(MongoDbContext context)
{
    private readonly IMongoCollection<User> _users = context.Users;

    public async Task<PagedResult<UserDto>> GetAllAsync(PageRequest page, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.Empty;

        // SELECT COUNT(*) + SELECT ... ORDER BY Name OFFSET x ROWS FETCH NEXT y ROWS ONLY
        var total = await _users.CountDocumentsAsync(filter, cancellationToken: ct);
        var users = await _users.Find(filter)
            .SortBy(u => u.Name)
            .Skip(page.Skip)
            .Limit(page.PageSize)
            .ToListAsync(ct);

        return new PagedResult<UserDto>(users.Select(UserDto.From).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<UserDto> GetByIdAsync(string id, CancellationToken ct)
    {
        return UserDto.From(await GetDocumentAsync(id, ct));
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = new User
        {
            Name = request.Name.Trim(),
            Email = NormalizeEmail(request.Email),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            // Id is null here: the driver generates the ObjectId and assigns it to user.Id.
            await _users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Raised by the unique index on "email" (E11000 duplicate key error).
            throw new ConflictException($"A user with email '{user.Email}' already exists.");
        }

        return UserDto.From(user);
    }

    /// <summary>
    /// UpdateOneAsync with $set modifies only the listed fields, like UPDATE ... SET in SQL.
    /// createdAt is left untouched without reading the document first.
    /// </summary>
    public async Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, CancellationToken ct)
    {
        var filter = Builders<User>.Filter.Eq(u => u.Id, id);
        var update = Builders<User>.Update
            .Set(u => u.Name, request.Name.Trim())
            .Set(u => u.Email, NormalizeEmail(request.Email));

        UpdateResult result;
        try
        {
            result = await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"A user with email '{NormalizeEmail(request.Email)}' already exists.");
        }

        // MatchedCount = documents found by the filter; ModifiedCount = documents actually changed.
        if (result.MatchedCount == 0)
        {
            throw new NotFoundException($"User '{id}' was not found.");
        }

        return await GetByIdAsync(id, ct);
    }

    /// <summary>
    /// Deletes a user and all of their expenses in a multi-document transaction.
    ///
    /// MongoDB has no ON DELETE CASCADE. A single-document write is always atomic, but here
    /// we touch two collections: without a transaction a failure in the middle could leave
    /// the user deleted and orphan expenses behind (or the other way round).
    ///
    /// Transactions require a replica set (or sharded cluster) because they are built on the
    /// oplog and on snapshot reads; that is why docker-compose starts MongoDB with --replSet.
    /// </summary>
    public async Task<DeleteUserResult> DeleteAsync(string id, CancellationToken ct)
    {
        // A session is the MongoDB equivalent of a connection with an open SqlTransaction.
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);

        // WithTransactionAsync starts the transaction, runs the callback, commits, and
        // retries the whole callback on transient errors (e.g. a write conflict or a
        // primary election). Any exception thrown inside aborts (rolls back) the transaction.
        var deletedExpenses = await session.WithTransactionAsync(
            async (s, token) =>
            {
                // Every operation must receive the session, otherwise it runs OUTSIDE the transaction.
                var userResult = await _users.DeleteOneAsync(
                    s, Builders<User>.Filter.Eq(u => u.Id, id), cancellationToken: token);

                if (userResult.DeletedCount == 0)
                {
                    throw new NotFoundException($"User '{id}' was not found.");
                }

                var expensesResult = await context.Expenses.DeleteManyAsync(
                    s, Builders<Expense>.Filter.Eq(e => e.UserId, id), cancellationToken: token);

                return expensesResult.DeletedCount;
            },
            new TransactionOptions(
                readConcern: ReadConcern.Snapshot,
                // "majority": the commit is acknowledged once replicated to most members.
                writeConcern: WriteConcern.WMajority),
            ct);

        return new DeleteUserResult(id, deletedExpenses);
    }

    public async Task EnsureExistsAsync(string id, CancellationToken ct)
    {
        // Limit = 1 stops counting at the first match: a cheap "EXISTS" check.
        var count = await _users.CountDocumentsAsync(
            Builders<User>.Filter.Eq(u => u.Id, id), new CountOptions { Limit = 1 }, ct);

        if (count == 0)
        {
            throw new NotFoundException($"User '{id}' was not found.");
        }
    }

    private async Task<User> GetDocumentAsync(string id, CancellationToken ct)
    {
        // Find(...) returns a fluent object; nothing is sent to the server until
        // FirstOrDefaultAsync / ToListAsync is called (similar to IQueryable deferred execution).
        return await _users.Find(u => u.Id == id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException($"User '{id}' was not found.");
    }

    // Emails are stored lowercased so the unique index behaves case-insensitively.
    // (Alternative: a unique index with a case-insensitive collation.)
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
