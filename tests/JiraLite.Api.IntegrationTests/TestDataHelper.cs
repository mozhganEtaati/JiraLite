using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;

namespace JiraLite.Api.IntegrationTests;

public static class TestDataHelper
{
    public sealed record RegisteredUser(Guid UserId, string Email, string AccessToken);

    public static async Task<RegisteredUser> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "Test_Passw0rd!";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        registerResponse.EnsureSuccessStatusCode();
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();

        return new RegisteredUser(
            registered.GetProperty("id").GetGuid(),
            email,
            login.GetProperty("accessToken").GetString()!);
    }

    public static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("Access token is null or empty");
        }

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {accessToken}");

        var orgResponse = await client.PostAsJsonAsync("/api/organizations", new { name = $"Org-{Guid.NewGuid():N}" });

        if (!orgResponse.IsSuccessStatusCode)
        {
            var content = await orgResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create organization: {orgResponse.StatusCode} - {content}");
        }

        var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orgId = org.GetProperty("id").GetGuid();

        var wsResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/workspaces", new { name = $"Workspace-{Guid.NewGuid():N}" });
        wsResponse.EnsureSuccessStatusCode();
        var workspace = await wsResponse.Content.ReadFromJsonAsync<JsonElement>();
        return workspace.GetProperty("id").GetGuid();
    }
}
