using System.ComponentModel.DataAnnotations;
using ExpenseManagement.Api.Models;

namespace ExpenseManagement.Api.Dtos;

public record CategoryDto(string Id, string Name, string? Description)
{
    public static CategoryDto From(Category category) => new(category.Id, category.Name, category.Description);
}

public record SaveCategoryRequest(
    [Required, StringLength(50, MinimumLength = 1)] string Name,
    [StringLength(250)] string? Description);
