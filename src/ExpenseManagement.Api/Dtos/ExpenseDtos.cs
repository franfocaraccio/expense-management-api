using System.ComponentModel.DataAnnotations;
using ExpenseManagement.Api.Models;

namespace ExpenseManagement.Api.Dtos;

public record ExpenseDto(
    string Id,
    string UserId,
    string CategoryId,
    decimal Amount,
    string? Description,
    DateTime Date,
    DateTime CreatedAt)
{
    public static ExpenseDto From(Expense e) =>
        new(e.Id, e.UserId, e.CategoryId, e.Amount, e.Description, e.Date, e.CreatedAt);
}

public record SaveExpenseRequest(
    [Required] string UserId,
    [Required] string CategoryId,
    [Range(typeof(decimal), "0.01", "1000000000")] decimal Amount,
    [StringLength(250)] string? Description,
    [Required] DateTime? Date);

/// <summary>Partial update: only the fields that are sent (not null) are modified.</summary>
public record PatchExpenseRequest(
    string? CategoryId,
    [Range(typeof(decimal), "0.01", "1000000000")] decimal? Amount,
    [StringLength(250)] string? Description,
    DateTime? Date);

public enum SortOrder
{
    Desc,
    Asc
}

/// <summary>Query string filters for expense searches.</summary>
public class ExpenseQuery : PageRequest
{
    public string? UserId { get; set; }

    public string? CategoryId { get; set; }

    /// <summary>Inclusive lower bound (UTC). Example: 2025-01-01</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound (UTC). Example: 2025-02-01 returns January only.</summary>
    public DateTime? To { get; set; }

    /// <summary>Sort by date. Default: newest first.</summary>
    public SortOrder Sort { get; set; } = SortOrder.Desc;
}
