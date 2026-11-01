namespace ExpenseManagement.Api.Common;

/// <summary>Base class for errors that map to a specific HTTP status code.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound);

public class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);

public class BadRequestException(string message) : AppException(message, StatusCodes.Status400BadRequest);
