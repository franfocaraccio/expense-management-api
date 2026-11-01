using System.ComponentModel.DataAnnotations;

namespace ExpenseManagement.Api.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class PageRequest
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>Number of documents to skip. Equivalent to OFFSET in SQL Server.</summary>
    public int Skip => (Page - 1) * PageSize;
}
