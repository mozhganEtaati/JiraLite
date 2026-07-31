using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace JiraLite.Api.Common.Behaviors;

/// <summary>
/// Catches any unhandled exception and shapes it into the RFC 7807 Problem Details
/// response defined in spec/19-api-guidelines.md §9. Never exposes exception details.
/// </summary>
public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Type = "https://jiralite.dev/errors/internal-server-error",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred."
            }
        });
    }
}
