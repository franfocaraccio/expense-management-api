using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseManagement.Api.Models;

/// <summary>
/// Stored in the "expenses" collection. UserId and CategoryId are references
/// (the MongoDB equivalent of foreign keys). MongoDB does NOT enforce them:
/// the application must validate that the referenced documents exist.
/// </summary>
public class Expense
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    // Stored as ObjectId (not string) so it matches users._id in $lookup and
    // uses 12 bytes instead of 24 characters in the document and in indexes.
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string CategoryId { get; set; } = null!;

    /// <summary>Stored as Decimal128 (see MongoDbConventions).</summary>
    public decimal Amount { get; set; }

    public string? Description { get; set; }

    /// <summary>When the expense happened. BSON dates are always stored in UTC.</summary>
    public DateTime Date { get; set; }

    public DateTime CreatedAt { get; set; }
}
