using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseManagement.Api.Dtos;

// These classes are deserialized straight from the aggregation output (BSON) and then
// returned as JSON. Field names match the $project stages (camelCase convention).

public class UserTotalDto
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;

    public string? UserName { get; set; }

    public string? Email { get; set; }

    public decimal Total { get; set; }

    public int Count { get; set; }

    public decimal Average { get; set; }
}

public class CategoryTotalDto
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string CategoryId { get; set; } = null!;

    public string? CategoryName { get; set; }

    public decimal Total { get; set; }

    public int Count { get; set; }

    public decimal Average { get; set; }
}

public class MonthlyTotalDto
{
    public int Year { get; set; }

    public int Month { get; set; }

    public decimal Total { get; set; }

    public int Count { get; set; }
}

/// <summary>Expense with its user and category resolved through $lookup.</summary>
public class ExpenseDetailsDto
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? Description { get; set; }

    public DateTime Date { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null when the referenced user no longer exists (no referential integrity).</summary>
    public UserSummaryDto? User { get; set; }

    public CategorySummaryDto? Category { get; set; }
}

public class UserSummaryDto
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;
}

public class CategorySummaryDto
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;
}
