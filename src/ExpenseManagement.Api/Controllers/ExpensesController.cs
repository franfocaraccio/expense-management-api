using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManagement.Api.Controllers;

[ApiController]
[Route("api/expenses")]
[Produces("application/json")]
public class ExpensesController(ExpenseService expenseService) : ControllerBase
{
    /// <summary>
    /// Searches expenses. All filters are optional and can be combined:
    /// userId, categoryId, from (inclusive), to (exclusive), sort (Desc/Asc by date), page, pageSize.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> Search([FromQuery] ExpenseQuery query, CancellationToken ct)
        => Ok(await expenseService.SearchAsync(query, ct));

    [HttpGet("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDto>> GetById(string id, CancellationToken ct)
        => Ok(await expenseService.GetByIdAsync(id, ct));

    /// <summary>
    /// Same filters as the search, but each expense embeds its user and category,
    /// resolved with $lookup in an aggregation pipeline.
    /// </summary>
    [HttpGet("details")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ExpenseDetailsDto>>> SearchWithDetails(
        [FromQuery] ExpenseQuery query, CancellationToken ct)
        => Ok(await expenseService.SearchWithDetailsAsync(query, ct));

    /// <summary>One expense with its user and category ($lookup).</summary>
    [HttpGet("{id:objectid}/details")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDetailsDto>> GetWithDetails(string id, CancellationToken ct)
        => Ok(await expenseService.GetWithDetailsAsync(id, ct));

    /// <summary>Creates an expense after checking that the user and the category exist.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDto>> Create(SaveExpenseRequest request, CancellationToken ct)
    {
        var expense = await expenseService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = expense.Id }, expense);
    }

    /// <summary>Replaces the whole expense document using ReplaceOneAsync.</summary>
    [HttpPut("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDto>> Replace(string id, SaveExpenseRequest request, CancellationToken ct)
        => Ok(await expenseService.ReplaceAsync(id, request, ct));

    /// <summary>Updates only the provided fields ($set built dynamically, FindOneAndUpdateAsync).</summary>
    [HttpPatch("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDto>> Patch(string id, PatchExpenseRequest request, CancellationToken ct)
        => Ok(await expenseService.PatchAsync(id, request, ct));

    [HttpDelete("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await expenseService.DeleteAsync(id, ct);
        return NoContent();
    }
}
