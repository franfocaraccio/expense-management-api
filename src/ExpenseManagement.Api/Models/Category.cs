using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseManagement.Api.Models;

/// <summary>Stored in the "categories" collection.</summary>
public class Category
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    // Optional fields can simply be missing from the document; here we store null.
    public string? Description { get; set; }
}
