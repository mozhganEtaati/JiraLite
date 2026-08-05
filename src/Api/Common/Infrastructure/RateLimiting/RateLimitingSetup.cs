using System.Globalization;
using System.Threading.RateLimiting;
using JiraLite.Api.Common.Auth;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Common.Infrastructure.RateLimiting;

/// <summary>
/// spec/19-api-guidelines.md §13 — a tight per-IP limit on <c>/api/auth/*</c>
/// (spec/01-authentication.md NFR-04) plus a baseline per-user limit on every other
/// <c>/api/*</c> endpoint. Rejections are RFC 7807 Problem Details with <c>Retry-After</c>,
/// matching §9-10's error contract.
/// </summary>
public static class RateLimitingSetup
{
    /// <summary>Named policy applied to the authentication endpoints via <c>RequireRateLimiting</c>.</summary>
    public const string AuthPolicyName = "auth";

    private const string NoLimitPartition = "unlimited";
    private const string RateLimitProblemType = "https://jiralite.dev/errors/rate-limit-exceeded";

    public static IServiceCollection AddJiraLiteRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Baseline per-user limit. Auth endpoints are excluded here because the tighter
            // named policy below already covers them — counting a login against both would
            // make the effective auth limit depend on how much other traffic the caller sent.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = ResolveOptions(context);
                if (!options.Enabled || !IsApiPath(context) || IsAuthPath(context))
                {
                    return RateLimitPartition.GetNoLimiter(NoLimitPartition);
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"global:{PartitionKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalPermitLimit,
                        Window = TimeSpan.FromSeconds(options.GlobalWindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            // Partitioned by IP, not user id: the callers this protects (credential stuffing,
            // registration floods) are unauthenticated by definition, so there is no user id
            // to key on and a per-account key would be trivially sidestepped.
            limiter.AddPolicy(AuthPolicyName, context =>
            {
                var options = ResolveOptions(context);
                if (!options.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter(NoLimitPartition);
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"auth:{ClientIp(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(options.AuthWindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            limiter.OnRejected = static async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
                    ? metadata
                    : TimeSpan.FromSeconds(ResolveOptions(httpContext).GlobalWindowSeconds);
                httpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                httpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(RateLimitingSetup))
                    .LogWarning(
                        "Rate limit exceeded for {Method} {Path} from {ClientIp}",
                        httpContext.Request.Method,
                        httpContext.Request.Path.Value,
                        ClientIp(httpContext));

                // Goes through IProblemDetailsService rather than Results.Problem so the
                // traceId extension configured in Program.cs is applied here too.
                var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = RateLimitProblemType,
                        Title = "Too Many Requests",
                        Status = StatusCodes.Status429TooManyRequests,
                        Detail = "Too many requests. Retry after the interval given in the Retry-After header.",
                        Instance = httpContext.Request.GetEncodedPathAndQuery()
                    }
                });
            };
        });

        return services;
    }

    // Resolved per request from the request container, never captured at builder time —
    // under WebApplicationFactory the test host layers its configuration in only once the
    // host is built, the same trap documented for the connection string in Program.cs.
    private static RateLimitingOptions ResolveOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    /// <summary>
    /// `/api` plus `/mcp` (spec/23-mcp-server.md NFR-03) — every caller-facing surface. `/health`
    /// and `/hangfire` are deliberately outside it.
    /// </summary>
    private static bool IsApiPath(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthPath(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);

    private static string PartitionKey(HttpContext context) =>
        context.User.TryGetUserId(out var userId)
            ? userId.ToString()
            : ClientIp(context);

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
