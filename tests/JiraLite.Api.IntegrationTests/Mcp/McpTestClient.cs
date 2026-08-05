using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace JiraLite.Api.IntegrationTests.Mcp;

/// <summary>
/// Connects a real MCP client to the test server over Streamable HTTP, authenticated with a real
/// Personal Access Token. Tests exercise the same path a configured editor would.
/// </summary>
public static class McpTestClient
{
    /// <summary>Issues a Personal Access Token for the caller identified by <paramref name="accessToken"/>.</summary>
    public static async Task<string> IssueTokenAsync(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {accessToken}");
        var response = await client.PostAsJsonAsync(
            "/api/users/me/tokens", new { name = "Integration test", expiresInDays = 30 });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    public static async Task<McpClient> ConnectAsync(McpEnabledApiFactory factory, string personalAccessToken)
    {
        // A dedicated HttpClient per connection: the transport keeps using it for the life of the
        // session, so sharing one with a test that rewrites its Authorization header would swap
        // the MCP session's credential mid-test.
        var httpClient = factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient);

        return await McpClient.CreateAsync(transport);
    }

    /// <summary>Calls a tool and asserts it succeeded, returning the result parsed as JSON.</summary>
    public static async Task<JsonElement> CallAsync(
        this McpClient client, string toolName, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(toolName, arguments!);
        if (result.IsError is true)
        {
            throw new InvalidOperationException($"Tool '{toolName}' failed: {TextOf(result)}");
        }

        return JsonSerializer.Deserialize<JsonElement>(TextOf(result));
    }

    /// <summary>Calls a tool expecting refusal, returning the error text so it can be asserted on.</summary>
    public static async Task<string> CallExpectingErrorAsync(
        this McpClient client, string toolName, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(toolName, arguments!);
        if (result.IsError is not true)
        {
            throw new InvalidOperationException($"Tool '{toolName}' unexpectedly succeeded: {TextOf(result)}");
        }

        return TextOf(result);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
