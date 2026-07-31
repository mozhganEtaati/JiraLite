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
        // BadHttpRequestException (e.g. malformed/truncated JSON body) already carries the
        // correct client-error status code — respect it instead of masking every such request
        // as a 500. Everything else is a genuine unexpected failure.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogWarning(exception, "Bad request for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

            httpContext.Response.StatusCode = badRequest.StatusCode;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails =
                {
                    Type = "https://jiralite.dev/errors/bad-request",
                    Title = "Bad Request",
                    Status = badRequest.StatusCode,
                    Detail = "The request could not be parsed."
                }
            });
        }

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
