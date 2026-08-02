using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Rbac;

/// <summary>
/// spec/16-rbac.md §15. Individual feature test classes each check the one 403 that matters to
/// them; these are the cross-cutting bullets that no single feature owns — the Viewer sweep, the
/// Workspace-Admin fallback, and the two places where a role is *not* enough (task T048).
/// The two Team bullets live in <see cref="Teams.TeamTests"/> alongside the rest of Teams.
/// </summary>
public class RbacTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public RbacTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_viewer_is_rejected_from_every_write_action_on_the_project()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        Authenticate(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var labelResponse = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/labels", new { name = "bug", color = "#E11D48" });
        labelResponse.EnsureSuccessStatusCode();
        var labelId = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");
        Authenticate(client, viewer.AccessToken);

        // One assertion per write surface the spec enumerates ("create Issue, comment, upload, etc.").
        var attempts = new (string Name, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("create issue", () => client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/issues", new { type = "Story", title = "No" })),
            ("edit issue", () => client.PatchAsJsonAsync($"/api/issues/{issueId}", new { title = "No" })),
            ("move issue", () => client.PatchAsJsonAsync($"/api/issues/{issueId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion = Convert.ToBase64String(new byte[8]) })),
            ("delete issue", () => client.DeleteAsync($"/api/issues/{issueId}")),
            ("add comment", () => client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "No" })),
            ("upload attachment", () => UploadAsync(client, issueId)),
            ("create label", () => client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/labels", new { name = "no", color = "#000000" })),
            ("attach label", () => client.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId })),
            ("create board", () => client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/boards", new { name = "No", type = "Kanban" })),
            ("edit project", () => client.PatchAsJsonAsync($"/api/projects/{seeded.ProjectId}", new { name = "No", description = (string?)null })),
            ("add project member", () => client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/members", new { userId = viewer.UserId, role = "ProjectAdmin" }))
        };

        var allowed = new List<string>();
        foreach (var (name, send) in attempts)
        {
            var response = await send();
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                allowed.Add($"{name} => {(int)response.StatusCode}");
            }
        }

        Assert.True(allowed.Count == 0, "A Viewer was not rejected with 403 by: " + string.Join(", ", allowed));

        // …while reads still work, which is the whole point of the role.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/projects/{seeded.ProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/issues/{issueId}")).StatusCode);
    }

    [Fact]
    public async Task A_project_admin_cannot_delete_the_project_but_a_workspace_admin_can()
    {
        var client = _factory.CreateClient();
        var workspaceAdmin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, workspaceAdmin.AccessToken);
        var projectAdmin = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, workspaceAdmin.AccessToken, "ProjectAdmin");

        // Archived first: deletion is rejected on a live Project for a different reason (409), and
        // that would mask the 403 this test is about.
        Authenticate(client, workspaceAdmin.AccessToken);
        (await client.PostAsync($"/api/projects/{seeded.ProjectId}/archive", null)).EnsureSuccessStatusCode();

        Authenticate(client, projectAdmin.AccessToken);
        var forbidden = await client.DeleteAsync($"/api/projects/{seeded.ProjectId}");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        Authenticate(client, workspaceAdmin.AccessToken);
        var allowed = await client.DeleteAsync($"/api/projects/{seeded.ProjectId}");

        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task A_workspace_admin_with_no_project_membership_acts_as_a_project_admin()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, owner.AccessToken);

        // A second Workspace Admin who is deliberately never added as a ProjectMember.
        var otherAdmin = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, owner.AccessToken);
        (await client.PostAsJsonAsync($"/api/workspaces/{seeded.WorkspaceId}/invitations", new { email = otherAdmin.Email, role = "Admin" }))
            .EnsureSuccessStatusCode();
        var token = await ReadInvitationTokenAsync(seeded.WorkspaceId, otherAdmin.Email);
        Authenticate(client, otherAdmin.AccessToken);
        (await client.PostAsync($"/api/invitations/{token}/accept", null)).EnsureSuccessStatusCode();

        var read = await client.GetAsync($"/api/projects/{seeded.ProjectId}");
        var create = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/issues", new { type = "Story", title = "By the workspace admin" });
        var manage = await client.PatchAsJsonAsync($"/api/projects/{seeded.ProjectId}", new { name = "Renamed", description = (string?)null });
        var myRole = await ReadJsonAsync(client, $"/api/projects/{seeded.ProjectId}/my-role");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, manage.StatusCode);
        // my-role reports the Workspace role plus the flag saying where it came from, rather than
        // pretending a ProjectMember row exists — the client needs to be able to tell the
        // difference, and the "as if they were ProjectAdmin" part of the criterion is what the
        // three calls above prove.
        Assert.Equal("Admin", myRole.GetProperty("effectiveRole").GetString());
        Assert.True(myRole.GetProperty("viaWorkspaceAdmin").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(db.ProjectMembers.Any(m => m.ProjectId == seeded.ProjectId && m.UserId == otherAdmin.UserId));
    }

    [Fact]
    public async Task Deleting_a_planned_sprint_is_a_project_admin_action_not_a_developer_one()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        Authenticate(client, admin.AccessToken);
        var boardResponse = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/boards", new { name = "Scrum", type = "Scrum" });
        boardResponse.EnsureSuccessStatusCode();
        var boardId = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");
        var projectAdmin = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "ProjectAdmin");

        Authenticate(client, developer.AccessToken);
        var sprintId = await CreateSprintAsync(client, boardId, admin);
        var denied = await client.DeleteAsync($"/api/sprints/{sprintId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Same Sprint, same request — only the caller's Project role differs.
        Authenticate(client, projectAdmin.AccessToken);
        var allowed = await client.DeleteAsync($"/api/sprints/{sprintId}");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task A_comment_author_demoted_to_viewer_can_no_longer_delete_their_own_comment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var author = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        Authenticate(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        Authenticate(client, author.AccessToken);
        var commentResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Mine." });
        commentResponse.EnsureSuccessStatusCode();
        var commentId = (await commentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // BR-06: authorship is evaluated against the caller's role *now*, not the role they held
        // when they wrote it.
        Authenticate(client, admin.AccessToken);
        (await client.PatchAsJsonAsync($"/api/projects/{seeded.ProjectId}/members/{author.UserId}", new { role = "Viewer" }))
            .EnsureSuccessStatusCode();

        Authenticate(client, author.AccessToken);
        var delete = await client.DeleteAsync($"/api/comments/{commentId}");

        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task A_user_with_no_membership_at_all_gets_a_null_effective_workspace_role()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, owner.AccessToken);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, outsider.AccessToken);
        var myRole = await ReadJsonAsync(client, $"/api/workspaces/{workspaceId}/my-role");

        Assert.Equal(JsonValueKind.Null, myRole.GetProperty("effectiveRole").ValueKind);
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string uri)
    {
        var response = await client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid issueId)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "shot.png");
        return await client.PostAsync($"/api/issues/{issueId}/attachments", content);
    }

    /// <summary>Creates a Sprint as the Admin and restores whatever authentication the caller had.</summary>
    private static async Task<Guid> CreateSprintAsync(HttpClient client, Guid boardId, TestDataHelper.RegisteredUser admin)
    {
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;
        Authenticate(client, admin.AccessToken);
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/sprints",
            new
            {
                name = $"Sprint-{Guid.NewGuid():N}",
                goal = (string?)null,
                plannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow),
                plannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14)
            });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = previousAuthorization;
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<string> ReadInvitationTokenAsync(Guid workspaceId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        return db.Invitations
            .Where(i => i.WorkspaceId == workspaceId && i.Email == email)
            .Select(i => i.Token)
            .Single();
    }
}
