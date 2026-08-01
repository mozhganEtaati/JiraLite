using Microsoft.Extensions.Configuration;

namespace JiraLite.Api.IntegrationTests.RateLimiting;

/// <summary>
/// The base factory disables rate limiting outright; this one turns it back on with limits
/// small enough to exercise in a test.
/// </summary>
public class RateLimitedApiFactory : JiraLiteApiFactory
{
    public const int AuthPermitLimit = 3;
    public const int GlobalPermitLimit = 5;

    /// <summary>
    /// Short, because the auth partition is keyed by client IP — the same loopback address for
    /// every test in the class — so each test has to wait out the previous one's window.
    /// </summary>
    public static readonly TimeSpan AuthWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Deliberately much longer than <see cref="AuthWindow"/>: the baseline limiter partitions
    /// by user id, so a test gets a fresh counter for free and never needs to wait one out.
    /// A short window here would instead be a flake source — the counter could replenish
    /// mid-test while the first request is still paying EF's query-compilation cost.
    /// </summary>
    public static readonly TimeSpan GlobalWindow = TimeSpan.FromSeconds(30);

    /// <summary>Long enough to guarantee the auth fixed window has replenished before the next request.</summary>
    public static Task WaitForFreshAuthWindowAsync() => Task.Delay(AuthWindow + TimeSpan.FromMilliseconds(750));

    protected override void ApplyAdditionalConfiguration(IConfigurationBuilder configBuilder) =>
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:AuthPermitLimit"] = AuthPermitLimit.ToString(),
            ["RateLimiting:AuthWindowSeconds"] = ((int)AuthWindow.TotalSeconds).ToString(),
            ["RateLimiting:GlobalPermitLimit"] = GlobalPermitLimit.ToString(),
            ["RateLimiting:GlobalWindowSeconds"] = ((int)GlobalWindow.TotalSeconds).ToString()
        });
}
