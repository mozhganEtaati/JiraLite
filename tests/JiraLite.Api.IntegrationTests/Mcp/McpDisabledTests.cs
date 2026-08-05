using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Mcp;

/// <summary>
/// spec/23-mcp-server.md NFR-05 — with the flag off, `/mcp` and the token endpoints do not exist
/// at all, and the rest of the API is unaffected. This class uses the base factory precisely
/// because it leaves <c>Mcp:Enabled</c> at its default.
/// </summary>
public class McpDisabledTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public McpDisabledTests(JiraLiteApiFactory factory) => _factory = factory;

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
    public async Task The_mcp_endpoint_is_not_mapped()
    {
        var client = await AuthenticatedClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task No_personal_access_token_can_be_minted()
    {
        var client = await AuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/users/me/tokens", new { name = "Nope", expiresInDays = 30 });
        var list = await client.GetAsync("/api/users/me/tokens");

        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task The_rest_of_the_api_is_unaffected()
    {
        var client = await AuthenticatedClientAsync();

        var profile = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }
}
