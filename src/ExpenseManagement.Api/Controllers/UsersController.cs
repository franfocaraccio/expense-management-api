using ExpenseManagement.Api.Dtos;
using ExpenseManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManagement.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
public class UsersController(UserService userService, ExpenseService expenseService) : ControllerBase
{
    /// <summary>Lists users ordered by name (paged).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> GetAll([FromQuery] PageRequest page, CancellationToken ct)
        => Ok(await userService.GetAllAsync(page, ct));

    [HttpGet("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(string id, CancellationToken ct)
        => Ok(await userService.GetByIdAsync(id, ct));

    /// <summary>Creates a user. Returns 409 if the email already exists (unique index).</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await userService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    /// <summary>Updates name and email using UpdateOneAsync + $set.</summary>
    [HttpPut("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Update(string id, UpdateUserRequest request, CancellationToken ct)
        => Ok(await userService.UpdateAsync(id, request, ct));

    /// <summary>
    /// Deletes the user and all of their expenses atomically (multi-document transaction).
    /// </summary>
    [HttpDelete("{id:objectid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeleteUserResult>> Delete(string id, CancellationToken ct)
        => Ok(await userService.DeleteAsync(id, ct));

    /// <summary>
    /// Expenses of a user, with optional category and date filters, sorting and paging.
    /// Served by the compound index { userId: 1, date: -1 }.
    /// </summary>
    [HttpGet("{id:objectid}/expenses")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetExpenses(
        string id, [FromQuery] ExpenseQuery query, CancellationToken ct)
    {
        await userService.EnsureExistsAsync(id, ct);
        query.UserId = id;
        return Ok(await expenseService.SearchAsync(query, ct));
    }
}
