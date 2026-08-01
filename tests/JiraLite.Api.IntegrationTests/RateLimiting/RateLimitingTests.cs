using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.RateLimiting;

/// <summary>
/// spec/19-api-guidelines.md §13 and spec/01-authentication.md NFR-04 (task T045).
/// </summary>
public class RateLimitingTests : IClassFixture<RateLimitedApiFactory>, IAsyncLifetime
{
    private readonly RateLimitedApiFactory _factory;

    public RateLimitingTests(RateLimitedApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Auth_requests_beyond_the_permit_limit_are_rejected_with_429()
    {
        await RateLimitedApiFactory.WaitForFreshAuthWindowAsync();
        var client = _factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.AuthPermitLimit; i++)
        {
            var allowed = await PostBadLoginAsync(client);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var rejected = await PostBadLoginAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task A_429_carries_Retry_After_and_a_problem_details_body()
    {
        await RateLimitedApiFactory.WaitForFreshAuthWindowAsync();
        var client = _factory.CreateClient();

        HttpResponseMessage? rejected = null;
        for (var i = 0; i <= RateLimitedApiFactory.AuthPermitLimit; i++)
        {
            rejected = await PostBadLoginAsync(client);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected!.StatusCode);

        // spec/19-api-guidelines.md §9-10 — errors are RFC 7807, including this one.
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.True(rejected.Headers.RetryAfter!.Delta > TimeSpan.Zero);

        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("Too Many Requests", problem.GetProperty("title").GetString());
        Assert.Equal("https://jiralite.dev/errors/rate-limit-exceeded", problem.GetProperty("type").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Auth_requests_are_allowed_again_once_the_window_elapses()
    {
        await RateLimitedApiFactory.WaitForFreshAuthWindowAsync();
        var client = _factory.CreateClient();

        for (var i = 0; i <= RateLimitedApiFactory.AuthPermitLimit; i++)
        {
            await PostBadLoginAsync(client);
        }

        await RateLimitedApiFactory.WaitForFreshAuthWindowAsync();

        var afterWindow = await PostBadLoginAsync(client);

        // 401, not 429 — the request reached the handler and failed on credentials instead.
        Assert.Equal(HttpStatusCode.Unauthorized, afterWindow.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_never_rate_limited()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < RateLimitedApiFactory.GlobalPermitLimit * 3; i++)
        {
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Baseline_limit_applies_to_non_auth_endpoints_and_is_partitioned_per_user()
    {
        await RateLimitedApiFactory.WaitForFreshAuthWindowAsync();
        var client = _factory.CreateClient();
        var first = await TestDataHelper.RegisterAndLoginAsync(client);

        // The second user's registration/login would trip the auth limit inside the same
        // window as the first user's, so start a new one.
        await RateLimitedApiFactory.WaitForFreshAuthWindowAsync();
        var second = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, first.AccessToken);
        for (var i = 0; i < RateLimitedApiFactory.GlobalPermitLimit; i++)
        {
            var allowed = await client.GetAsync("/api/users/me");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var rejected = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // Same window, same IP, different user — a separate partition, so still allowed.
        Authenticate(client, second.AccessToken);
        var otherUser = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {accessToken}");

    private static Task<HttpResponseMessage> PostBadLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "Wrong_Passw0rd!" });
}
