namespace JiraLite.Api.Common.Infrastructure.RateLimiting;

/// <summary>
/// Rate-limit thresholds. spec/19-api-guidelines.md §13 deliberately does not enumerate
/// per-endpoint limits — it defers them to configuration, which is what this class binds.
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Kill switch for the whole limiter. Off only for integration tests, which register and
    /// log in hundreds of times per run and would otherwise limit themselves out.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Requests per window allowed against <c>/api/auth/*</c>, per client IP (spec/01-authentication.md NFR-04).</summary>
    public int AuthPermitLimit { get; set; } = 10;

    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>Baseline requests per window allowed against every other <c>/api/*</c> endpoint, per user (spec/19-api-guidelines.md §13).</summary>
    public int GlobalPermitLimit { get; set; } = 300;

    public int GlobalWindowSeconds { get; set; } = 60;
}
