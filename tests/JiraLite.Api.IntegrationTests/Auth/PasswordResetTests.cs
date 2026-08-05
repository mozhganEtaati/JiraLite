using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Auth;

/// <summary>
/// spec/01-authentication.md §15 — the password reset acceptance criteria (FR-06, FR-07,
/// BR-09–BR-12, NFR-06).
///
/// The raw token never leaves the API in a response, so these tests read the row and reconstruct
/// what the email would have carried, the same way the token itself is redeemed.
/// </summary>
public class PasswordResetTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private const string OriginalPassword = "Test_Passw0rd!";
    private const string NewPassword = "Replaced_Passw0rd9";

    private readonly JiraLiteApiFactory _factory;

    public PasswordResetTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_unknown_address_is_answered_exactly_like_a_registered_one_and_mints_nothing()
    {
        var client = _factory.CreateClient();
        var registered = NewEmail();
        await RegisterAsync(client, registered);

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = registered });
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = NewEmail() });

        // Same status and same body — anything else and this endpoint becomes the account
        // enumeration oracle that login (NFR-03) deliberately is not.
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        // Exactly one row: the registered address only.
        Assert.Equal(1, await db.PasswordResetTokens.CountAsync());
    }

    [Fact]
    public async Task A_requested_reset_persists_the_token_hashed_never_in_the_clear()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);

        var rawToken = await RequestResetAsync(client, email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var stored = await db.PasswordResetTokens.SingleAsync();

        Assert.Equal(userId, stored.UserId);
        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.Equal(PasswordResetTokenGenerator.Hash(rawToken), stored.TokenHash);
        Assert.Null(stored.UsedAtUtc);
        Assert.True(stored.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Completing_a_reset_makes_the_new_password_work_and_the_old_one_fail()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var rawToken = await RequestResetAsync(client, email);

        var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var withNew = await client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword });
        var withOld = await client.PostAsJsonAsync("/api/auth/login", new { email, password = OriginalPassword });

        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_be_redeemed_twice()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var rawToken = await RequestResetAsync(client, email);

        var first = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });
        first.EnsureSuccessStatusCode();

        var replay = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = "Third_Passw0rd7" });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // The replay changed nothing — the password set by the first redemption still stands.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var rawToken = await RequestResetAsync(client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            var stored = await db.PasswordResetTokens.SingleAsync();
            // Backdated rather than waiting out the configured lifetime.
            db.Entry(stored).Property(t => t.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = OriginalPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Requesting_a_second_link_invalidates_the_first()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var firstToken = await RequestResetAsync(client, email);
        var secondToken = await RequestResetAsync(client, email);

        Assert.NotEqual(firstToken, secondToken);

        var withFirst = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = firstToken, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, withFirst.StatusCode);

        var withSecond = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = secondToken, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, withSecond.StatusCode);
    }

    [Fact]
    public async Task Completing_a_reset_revokes_every_live_session()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        // A session established before the reset — the one an attacker holding the old password
        // would have.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = OriginalPassword });
        login.EnsureSuccessStatusCode();
        var refreshToken = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString()!;

        var rawToken = await RequestResetAsync(client, email);
        var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });
        reset.EnsureSuccessStatusCode();

        var refreshAfterReset = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterReset.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_account_gets_the_same_answer_but_no_token()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = OriginalPassword });
        login.EnsureSuccessStatusCode();
        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        using var deactivate = new HttpRequestMessage(HttpMethod.Post, "/api/users/me/deactivate");
        deactivate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        (await client.SendAsync(deactivate)).EnsureSuccessStatusCode();

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        // BR-12: indistinguishable from the healthy path, but a reset must not be a way back into
        // an account that can no longer authenticate.
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.PasswordResetTokens.AnyAsync());
    }

    [Fact]
    public async Task A_new_password_that_registration_would_refuse_is_refused_here_too()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var rawToken = await RequestResetAsync(client, email);

        var tooShort = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = "Ab1" });
        var noDigit = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = "NoDigitsHere" });

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noDigit.StatusCode);

        // Rejected before the handler ran, so the token is still unspent.
        var accepted = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    private static string NewEmail() => $"reset-{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = OriginalPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Requests a reset and returns the raw token, read back out of the email the request enqueued.
    ///
    /// The row stores only a hash and no endpoint ever returns the raw value, so the mail body is
    /// genuinely the only place it exists — which makes this the same path a real user takes, and
    /// means a change that stopped sending the token would fail these tests rather than pass them.
    /// Reading Hangfire's job table is the established way to observe a dispatched email here; see
    /// NotificationDeliveryTests.AnyEmailJobForAsync.
    /// </summary>
    private async Task<string> RequestResetAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        response.EnsureSuccessStatusCode();

        var body = await ReadLatestResetEmailBodyAsync(email);

        // 32 bytes as lowercase hex — PasswordResetTokenGenerator's output, and the only such run
        // in the message.
        var match = Regex.Match(body, "[0-9a-f]{64}");
        Assert.True(match.Success, $"No reset token found in the email body: {body}");
        return match.Value;
    }

    private async Task<string> ReadLatestResetEmailBodyAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        // Arguments is a JSON array of SendEmailJob.Execute's parameters: [toEmail, subject, body, token].
        const string sql = """
            SELECT TOP 1 Arguments
            FROM [HangFire].[Job]
            WHERE InvocationData LIKE '%SendEmailJob%'
              AND Arguments LIKE @emailPattern
              AND Arguments LIKE '%Reset your JiraLite password%'
            ORDER BY Id DESC
            """;

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@emailPattern", $"%{email}%");
            var arguments = (string?)await command.ExecuteScalarAsync();
            Assert.True(arguments is not null, $"No password reset email was enqueued for {email}.");
            return arguments!;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
