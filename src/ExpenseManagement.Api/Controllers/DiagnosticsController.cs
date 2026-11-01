using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManagement.Api.Controllers;

/// <summary>Query performance: explain() plans and index information.</summary>
[ApiController]
[Route("api/diagnostics")]
[Produces("application/json")]
public class DiagnosticsController(DiagnosticsService diagnosticsService) : ControllerBase
{
    /// <summary>
    /// Explains "latest expenses of a user" with the planner's choice (compound index
    /// { userId: 1, date: -1 }) and with a forced collection scan, side by side.
    /// </summary>
    /// <param name="userId">Any user id (see GET /api/users).</param>
    /// <param name="from">Optional inclusive lower bound.</param>
    /// <param name="to">Optional exclusive upper bound.</param>
    /// <param name="includeRaw">Include the full explain output.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("explain/expenses-by-user")]
    public async Task<ActionResult<ExplainComparisonDto>> ExplainByUser(
        [FromQuery] string userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] bool includeRaw, CancellationToken ct)
        => Ok(await diagnosticsService.ExplainExpensesByUserAsync(userId, from, to, includeRaw, ct));

    /// <summary>
    /// Explains "latest expenses of a category": uses { categoryId: 1 } but still needs an
    /// in-memory SORT, a good example of why index key order matters.
    /// </summary>
    [HttpGet("explain/expenses-by-category")]
    public async Task<ActionResult<ExplainComparisonDto>> ExplainByCategory(
        [FromQuery] string categoryId, [FromQuery] bool includeRaw, CancellationToken ct)
        => Ok(await diagnosticsService.ExplainExpensesByCategoryAsync(categoryId, includeRaw, ct));

    /// <summary>Lists the indexes of every collection with their usage counters ($indexStats).</summary>
    [HttpGet("indexes")]
    public async Task<ActionResult<IReadOnlyList<IndexInfoDto>>> Indexes(CancellationToken ct)
        => Ok(await diagnosticsService.GetIndexesAsync(ct));
}
