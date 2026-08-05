using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Users;

/// <summary>
/// spec/23-mcp-server.md §15 token criteria — FR-03 (the plaintext appears exactly once),
/// BR-03 (bounded lifetime), BR-04 (revocation is idempotent), BR-05 (at most 10 active).
/// </summary>
public class AccessTokenTests : IClassFixture<McpEnabledApiFactory>, IAsyncLifetime
{
    private readonly McpEnabledApiFactory _factory;

    public AccessTokenTests(McpEnabledApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {user.AccessToken}");
        return client;
    }

    [Fact]
    public async Task The_plaintext_value_is_returned_once_at_creation_and_never_again()
    {
        var client = await AuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync(
            "/api/users/me/tokens", new { name = "Work laptop", expiresInDays = 90 });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var plaintext = created.GetProperty("token").GetString()!;
        Assert.StartsWith("jlp_", plaintext);

        var listResponse = await client.GetAsync("/api/users/me/tokens");
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("\"token\"", listBody);
        Assert.DoesNotContain(plaintext, listBody);
    }

    [Fact]
    public async Task Only_the_hash_is_persisted()
    {
        var client = await AuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync(
            "/api/users/me/tokens", new { name = "Work laptop", expiresInDays = 30 });
        createResponse.EnsureSuccessStatusCode();
        var plaintext = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var stored = await db.PersonalAccessTokens.SingleAsync();

        Assert.NotEqual(plaintext, stored.TokenHash);
        Assert.DoesNotContain(plaintext, stored.TokenHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    [InlineData(-1)]
    public async Task Lifetime_outside_one_to_three_hundred_sixty_five_days_is_rejected(int expiresInDays)
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/users/me/tokens", new { name = "Out of range", expiresInDays });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_eleventh_active_token_is_rejected_and_the_existing_ten_survive()
    {
        var client = await AuthenticatedClientAsync();

        for (var i = 0; i < 10; i++)
        {
            var ok = await client.PostAsJsonAsync(
                "/api/users/me/tokens", new { name = $"Token {i}", expiresInDays = 30 });
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var eleventh = await client.PostAsJsonAsync(
            "/api/users/me/tokens", new { name = "One too many", expiresInDays = 30 });

        Assert.Equal(HttpStatusCode.Conflict, eleventh.StatusCode);

        var listResponse = await client.GetAsync("/api/users/me/tokens?limit=100");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var active = list.GetProperty("items").EnumerateArray().Count(t => t.GetProperty("isActive").GetBoolean());
        Assert.Equal(10, active);
    }

    [Fact]
    public async Task Revoking_is_idempotent_and_marks_the_token_inactive()
    {
        var client = await AuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/users/me/tokens", new { name = "Revoke me", expiresInDays = 30 });
        var tokenId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var first = await client.DeleteAsync($"/api/users/me/tokens/{tokenId}");
        var second = await client.DeleteAsync($"/api/users/me/tokens/{tokenId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var list = await (await client.GetAsync("/api/users/me/tokens")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(list.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Another_users_token_id_is_not_found_rather_than_forbidden()
    {
        var owner = await AuthenticatedClientAsync();
        var created = await owner.PostAsJsonAsync("/api/users/me/tokens", new { name = "Mine", expiresInDays = 30 });
        var tokenId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var stranger = await AuthenticatedClientAsync();
        var response = await stranger.DeleteAsync($"/api/users/me/tokens/{tokenId}");

        // 404, not 403 — a 403 would confirm the id exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_users_token_list_never_includes_another_users_tokens()
    {
        var owner = await AuthenticatedClientAsync();
        await owner.PostAsJsonAsync("/api/users/me/tokens", new { name = "Mine", expiresInDays = 30 });

        var stranger = await AuthenticatedClientAsync();
        var list = await (await stranger.GetAsync("/api/users/me/tokens")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }
}
