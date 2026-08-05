using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Auth;

/// <summary>
/// spec/23-mcp-server.md BR-02 (the two credential types are non-interchangeable), BR-04, BR-07,
/// BR-08, and the §13 rows for an unusable credential on /mcp.
///
/// These use raw JSON-RPC over /mcp rather than an MCP client: the point is which credential the
/// endpoint accepts, and a client would fail during its own handshake for reasons unrelated to it.
/// </summary>
public class PersonalAccessTokenAuthTests : IClassFixture<McpEnabledApiFactory>, IAsyncLifetime
{
    private readonly McpEnabledApiFactory _factory;

    public PersonalAccessTokenAuthTests(McpEnabledApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static HttpRequestMessage McpRequest(string? credential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (credential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        return request;
    }

    private async Task<(HttpClient Client, TestDataHelper.RegisteredUser User, string Pat, Guid TokenId)> SeedAsync()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {user.AccessToken}");

        var created = await client.PostAsJsonAsync("/api/users/me/tokens", new { name = "MCP", expiresInDays = 30 });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        return (client, user, body.GetProperty("token").GetString()!, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_personal_access_token_is_rejected_by_the_rest_api()
    {
        var (client, _, pat, _) = await SeedAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_jwt_access_token_is_rejected_by_the_mcp_endpoint()
    {
        var (client, user, _, _) = await SeedAsync();

        var response = await client.SendAsync(McpRequest(user.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_personal_access_token_is_accepted_by_the_mcp_endpoint()
    {
        var (client, _, pat, _) = await SeedAsync();

        var response = await client.SendAsync(McpRequest(pat));

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}.");
    }

    [Fact]
    public async Task No_credential_is_rejected()
    {
        var (client, _, _, _) = await SeedAsync();

        var response = await client.SendAsync(McpRequest(null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        var (client, _, _, _) = await SeedAsync();

        var response = await client.SendAsync(McpRequest("jlp_" + new string('a', 64)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_revoked_token_stops_working_immediately()
    {
        var (client, _, pat, tokenId) = await SeedAsync();
        Assert.True((await client.SendAsync(McpRequest(pat))).IsSuccessStatusCode);

        var revoke = await client.DeleteAsync($"/api/users/me/tokens/{tokenId}");
        revoke.EnsureSuccessStatusCode();

        var response = await client.SendAsync(McpRequest(pat));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var (client, _, pat, tokenId) = await SeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            await db.PersonalAccessTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        var response = await client.SendAsync(McpRequest(pat));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_owners_tokens_stop_working_without_being_revoked()
    {
        var (client, _, pat, tokenId) = await SeedAsync();

        var deactivate = await client.PostAsync("/api/users/me/deactivate", null);
        deactivate.EnsureSuccessStatusCode();

        var response = await client.SendAsync(McpRequest(pat));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // BR-07 is about the owner's state, not about revocation — the row is untouched.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.Null(await db.PersonalAccessTokens.Where(t => t.Id == tokenId).Select(t => t.RevokedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Last_used_is_recorded_on_first_use()
    {
        var (client, _, pat, tokenId) = await SeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            Assert.Null(await db.PersonalAccessTokens.Where(t => t.Id == tokenId).Select(t => t.LastUsedAtUtc).SingleAsync());
        }

        var response = await client.SendAsync(McpRequest(pat));
        Assert.True(response.IsSuccessStatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            Assert.NotNull(await db.PersonalAccessTokens.Where(t => t.Id == tokenId).Select(t => t.LastUsedAtUtc).SingleAsync());
        }
    }
}
