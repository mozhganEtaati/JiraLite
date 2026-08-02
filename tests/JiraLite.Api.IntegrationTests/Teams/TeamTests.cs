using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Teams;

/// <summary>
/// spec/04-teams.md §15, plus the two Team-related bullets in spec/16-rbac.md §15. Teams shipped in
/// Phase 2 with no integration coverage at all — this file is the whole of it (task T048).
/// </summary>
public class TeamTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public TeamTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_workspace_admin_creates_a_team_and_it_starts_with_no_members()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        var teamId = await CreateTeamAsync(client, workspaceId);
        var team = await ReadJsonAsync(client, $"/api/teams/{teamId}");

        Assert.Empty(team.GetProperty("members").EnumerateArray());
        Assert.Equal(workspaceId, team.GetProperty("workspaceId").GetGuid());
    }

    [Fact]
    public async Task A_team_lead_adds_another_workspace_member_without_any_admin_involvement()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var teamId = await CreateTeamAsync(client, workspaceId);

        var lead = await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken);
        var recruit = await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken);

        Authenticate(client, admin.AccessToken);
        (await client.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = lead.UserId })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/teams/{teamId}/members/{lead.UserId}", new { isLead = true })).EnsureSuccessStatusCode();

        // From here on the Admin does nothing — the Lead acts alone.
        Authenticate(client, lead.AccessToken);
        var add = await client.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = recruit.UserId });

        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        var team = await ReadJsonAsync(client, $"/api/teams/{teamId}");
        Assert.Contains(recruit.UserId, team.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("userId").GetGuid()));
    }

    [Fact]
    public async Task Adding_someone_who_is_not_a_workspace_member_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var teamId = await CreateTeamAsync(client, workspaceId);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, admin.AccessToken);
        var add = await client.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = outsider.UserId });

        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_team_removes_its_memberships_and_nothing_else()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var teamId = await CreateTeamAsync(client, workspaceId);
        var member = await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken);

        Authenticate(client, admin.AccessToken);
        (await client.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = member.UserId })).EnsureSuccessStatusCode();

        var delete = await client.DeleteAsync($"/api/teams/{teamId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.Teams.AnyAsync(t => t.Id == teamId));
        Assert.False(await db.TeamMembers.AnyAsync(m => m.TeamId == teamId));
        // BR-05: the User and their Workspace membership are untouched.
        Assert.True(await db.Users.AnyAsync(u => u.Id == member.UserId));
        Assert.True(await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == member.UserId));
    }

    [Fact]
    public async Task Being_a_team_lead_grants_no_project_access_of_its_own()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var teamId = await CreateTeamAsync(client, seeded.WorkspaceId);
        var lead = await InviteAndAcceptAsync(client, seeded.WorkspaceId, admin.AccessToken);

        Authenticate(client, admin.AccessToken);
        (await client.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = lead.UserId })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/teams/{teamId}/members/{lead.UserId}", new { isLead = true })).EnsureSuccessStatusCode();
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        // BR-03/spec/16-rbac.md BR-05 — Team membership is an organisational grouping, never a
        // permission grant. The Lead holds no ProjectMember row on this Project.
        Authenticate(client, lead.AccessToken);
        var read = await client.GetAsync($"/api/projects/{seeded.ProjectId}");
        var write = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/issues", new { type = "Story", title = "Should not be allowed" });
        var comment = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Should not be allowed" });
        var myRole = await ReadJsonAsync(client, $"/api/projects/{seeded.ProjectId}/my-role");

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comment.StatusCode);
        Assert.Equal(JsonValueKind.Null, myRole.GetProperty("effectiveRole").ValueKind);
    }

    [Fact]
    public async Task A_workspace_member_who_is_not_on_the_team_can_still_view_it()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var teamId = await CreateTeamAsync(client, workspaceId);
        var bystander = await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken);

        // spec/16-rbac.md §15: viewing a Team is Workspace-scoped, not Team-scoped.
        Authenticate(client, bystander.AccessToken);
        var get = await client.GetAsync($"/api/teams/{teamId}");
        var list = await client.GetAsync($"/api/workspaces/{workspaceId}/teams");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task A_plain_member_cannot_create_rename_or_delete_a_team()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var teamId = await CreateTeamAsync(client, workspaceId);
        var member = await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken);

        Authenticate(client, member.AccessToken);
        var create = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/teams", new { name = "Nope", description = (string?)null });
        var rename = await client.PatchAsJsonAsync($"/api/teams/{teamId}", new { name = "Nope", description = (string?)null });
        var delete = await client.DeleteAsync($"/api/teams/{teamId}");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static async Task<Guid> CreateTeamAsync(HttpClient client, Guid workspaceId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/teams", new { name = $"Team-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string uri)
    {
        var response = await client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Registers a user and gets them into the Workspace as a plain Member. Leaves the client unauthenticated.</summary>
    private async Task<TestDataHelper.RegisteredUser> InviteAndAcceptAsync(HttpClient client, Guid workspaceId, string adminAccessToken)
    {
        var invitee = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, adminAccessToken);
        (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/invitations", new { email = invitee.Email, role = "Member" }))
            .EnsureSuccessStatusCode();

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            token = await db.Invitations
                .Where(i => i.WorkspaceId == workspaceId && i.Email == invitee.Email && i.Status == InvitationStatus.Pending)
                .Select(i => i.Token)
                .SingleAsync();
        }

        Authenticate(client, invitee.AccessToken);
        (await client.PostAsync($"/api/invitations/{token}/accept", null)).EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;
        return invitee;
    }
}
