using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Auth;

/// <summary>
/// spec/01-authentication.md §15, every bullet. Registration and login were previously exercised
/// only incidentally, as setup inside <see cref="TestDataHelper"/>; token rotation, reuse
/// detection, logout and the deactivated-account path had no coverage at all (task T048).
/// </summary>
public class AuthenticationTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private const string Password = "Test_Passw0rd!";

    private readonly JiraLiteApiFactory _factory;

    public AuthenticationTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Registering_creates_the_user_without_issuing_any_token()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());

        // "Register does not auto-login": no token of any kind in the response…
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.False(body.TryGetProperty("refreshToken", out _));

        // …and nothing was persisted for them either.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var userId = body.GetProperty("id").GetGuid();
        Assert.True(await db.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task Logging_in_returns_a_token_pair_and_persists_the_refresh_token_hashed()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rawRefreshToken = body.GetProperty("refreshToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(rawRefreshToken));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var stored = await db.RefreshTokens.SingleAsync();
        // Stored hashed, never in the clear — the raw value must not be recoverable from the row.
        Assert.NotEqual(rawRefreshToken, stored.TokenHash);
        Assert.Equal(tokenService.HashRefreshToken(rawRefreshToken), stored.TokenHash);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task Refreshing_rotates_the_token_and_links_the_old_one_to_its_replacement()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var first = await LoginAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newRefreshToken = body.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first.RefreshToken, newRefreshToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var oldToken = await db.RefreshTokens.SingleAsync(t => t.TokenHash == tokenService.HashRefreshToken(first.RefreshToken));
        var replacement = await db.RefreshTokens.SingleAsync(t => t.TokenHash == tokenService.HashRefreshToken(newRefreshToken));

        Assert.NotNull(oldToken.RevokedAtUtc);
        Assert.Equal(replacement.Id, oldToken.ReplacedByTokenId);
        Assert.Null(replacement.RevokedAtUtc);
    }

    [Fact]
    public async Task Reusing_a_revoked_refresh_token_revokes_every_active_token_for_that_user()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var first = await LoginAsync(client, email);

        // Rotate once, so `first` is revoked and a live token exists alongside it.
        var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first.RefreshToken });
        rotated.EnsureSuccessStatusCode();

        // A second, independent session that must also be killed by the reuse detection.
        var otherSession = await LoginAsync(client, email);

        var replayed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == userId && t.RevokedAtUtc == null));
        }

        // The still-unused token from the other session is dead too, not merely marked in the table.
        var afterFamilyRevocation = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = otherSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterFamilyRevocation.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_presented_token_and_a_later_refresh_with_it_fails()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var session = await LoginAsync(client, email);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = session.RefreshToken })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var logout = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refreshAfterLogout = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_account_fails_login_with_the_same_message_as_a_wrong_password()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var session = await LoginAsync(client, email);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var deactivate = await client.PostAsync("/api/users/me/deactivate", null);
        deactivate.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;

        var deactivatedLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var wrongPasswordLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = NewEmail(), password = "Wrong_Passw0rd!" });

        Assert.Equal(HttpStatusCode.Unauthorized, deactivatedLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordLogin.StatusCode);

        // Indistinguishable bodies: anything else would let an attacker enumerate accounts.
        var deactivatedDetail = await ReadDetailAsync(deactivatedLogin);
        var wrongPasswordDetail = await ReadDetailAsync(wrongPasswordLogin);
        Assert.Equal(wrongPasswordDetail, deactivatedDetail);
    }

    [Fact]
    public async Task Registering_an_email_that_already_exists_is_rejected_with_409()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        // Case-insensitively: BR-01 treats the address as the identity, not the exact string.
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = Password });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static string NewEmail() => $"auth-{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private sealed record Session(string AccessToken, string RefreshToken);

    private static async Task<Session> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Session(body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }
}
