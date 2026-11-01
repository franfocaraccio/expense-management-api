using System.ComponentModel.DataAnnotations;
using ExpenseManagement.Api.Models;

namespace ExpenseManagement.Api.Dtos;

public record UserDto(string Id, string Name, string Email, DateTime CreatedAt)
{
    public static UserDto From(User user) => new(user.Id, user.Name, user.Email, user.CreatedAt);
}

public record CreateUserRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, EmailAddress, StringLength(200)] string Email);

public record DeleteUserResult(string UserId, long DeletedExpenses);

public record UpdateUserRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, EmailAddress, StringLength(200)] string Email);
