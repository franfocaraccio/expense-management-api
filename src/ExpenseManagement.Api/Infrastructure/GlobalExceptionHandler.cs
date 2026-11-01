using ExpenseManagement.Api.Common;
using Microsoft.AspNetCore.Diagnostics;
using MongoDB.Driver;

namespace ExpenseManagement.Api.Infrastructure;

/// <summary>Converts exceptions into RFC 7807 ProblemDetails responses.</summary>
public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            AppException app => (app.StatusCode, app.Message),
            // Safety net: a unique index violation not handled by a service (error code 11000).
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } =>
                (StatusCodes.Status409Conflict, "A document with the same unique key already exists."),
            TimeoutException => (StatusCodes.Status503ServiceUnavailable, "The database is not reachable."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception");
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title }
        });
    }
}
