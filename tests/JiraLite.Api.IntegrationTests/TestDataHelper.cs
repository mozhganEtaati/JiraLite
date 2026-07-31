using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    public sealed record SeededProject(Guid WorkspaceId, Guid ProjectId, Guid BoardId, Guid DefaultColumnId, Guid DoneColumnId);

    /// <summary>Creates a Workspace + Project (with its auto-created default Board/columns) as the given caller. Used as setup by every Work Tracking test.</summary>
    public static async Task<SeededProject> CreateProjectAsync(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {accessToken}");
        var workspaceId = await CreateWorkspaceAsync(client, accessToken);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/projects",
            new { key = "JIRA", name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        createResponse.EnsureSuccessStatusCode();
        var project = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = project.GetProperty("id").GetGuid();

        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        boardsResponse.EnsureSuccessStatusCode();
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items")[0].GetProperty("id").GetGuid();

        var boardResponse = await client.GetAsync($"/api/boards/{boardId}");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var columns = board.GetProperty("columns").EnumerateArray().ToList();
        var defaultColumnId = columns.First(c => c.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var doneColumnId = columns.First(c => c.GetProperty("isDoneColumn").GetBoolean()).GetProperty("id").GetGuid();

        return new SeededProject(workspaceId, projectId, boardId, defaultColumnId, doneColumnId);
    }

    /// <summary>
    /// Registers a new user, invites+accepts them into the Workspace, and adds them as a Project
    /// member with the given role. Leaves the client authenticated as that new member. Restores the
    /// admin's auth afterward is the caller's responsibility if further admin calls are needed.
    /// </summary>
    public static async Task<RegisteredUser> AddProjectMemberAsync(
        HttpClient client, JiraLiteApiFactory factory, Guid workspaceId, Guid projectId, string adminAccessToken, string projectRole)
    {
        var member = await RegisterAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {adminAccessToken}");
        var inviteResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/invitations", new { email = member.Email, role = "Member" });
        inviteResponse.EnsureSuccessStatusCode();

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            token = await db.Invitations
                .Where(i => i.WorkspaceId == workspaceId && i.Email == member.Email)
                .Select(i => i.Token)
                .SingleAsync();
        }

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {member.AccessToken}");
        var acceptResponse = await client.PostAsync($"/api/invitations/{token}/accept", null);
        acceptResponse.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {adminAccessToken}");
        var addResponse = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { userId = member.UserId, role = projectRole });
        addResponse.EnsureSuccessStatusCode();

        return member;
    }

    public static async Task<Guid> CreateIssueAsync(
        HttpClient client, Guid projectId, string type = "Story", Guid? parentIssueId = null, string? title = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues",
            new { type, title = title ?? $"Issue-{Guid.NewGuid():N}", parentIssueId });
        response.EnsureSuccessStatusCode();
        var issue = await response.Content.ReadFromJsonAsync<JsonElement>();
        return issue.GetProperty("id").GetGuid();
    }
}
