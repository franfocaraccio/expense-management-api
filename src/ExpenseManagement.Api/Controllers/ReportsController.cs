using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManagement.Api.Controllers;

/// <summary>Reports computed on the server with aggregation pipelines.</summary>
[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public class ReportsController(ReportService reportService) : ControllerBase
{
    /// <summary>Total, count and average spent per user ($group + $lookup users).</summary>
    /// <param name="from">Inclusive lower bound, e.g. 2025-01-01</param>
    /// <param name="to">Exclusive upper bound, e.g. 2026-01-01</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("totals-by-user")]
    public async Task<ActionResult<IReadOnlyList<UserTotalDto>>> TotalsByUser(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await reportService.GetTotalsByUserAsync(from, to, ct));

    /// <summary>Expenses grouped by category ($group + $lookup categories).</summary>
    [HttpGet("totals-by-category")]
    public async Task<ActionResult<IReadOnlyList<CategoryTotalDto>>> TotalsByCategory(
        [FromQuery] string? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await reportService.GetTotalsByCategoryAsync(userId, from, to, ct));

    /// <summary>Monthly totals ($group by { $year, $month }), optionally for one user and year.</summary>
    [HttpGet("monthly-totals")]
    public async Task<ActionResult<IReadOnlyList<MonthlyTotalDto>>> MonthlyTotals(
        [FromQuery] string? userId, [FromQuery] int? year, CancellationToken ct)
        => Ok(await reportService.GetMonthlyTotalsAsync(userId, year, ct));
}
