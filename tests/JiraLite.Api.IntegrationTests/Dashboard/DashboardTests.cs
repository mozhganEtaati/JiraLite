using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Dashboard;

/// <summary>
/// spec/14-dashboard.md §15 — My Tasks, My Projects, and Recent Activity.
/// All three views share one class (and therefore one SQL Server container) deliberately: each
/// JiraLiteApiFactory starts its own container, so splitting read-only views across classes
/// multiplies suite runtime for no isolation benefit.
/// </summary>
public class DashboardTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DashboardTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<(Guid IssueId, string RowVersion)> CreateAssignedIssueAsync(
        HttpClient client, Guid projectId, Guid assigneeUserId)
    {
        var createResponse = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues",
            new { type = "Task", title = $"Task-{Guid.NewGuid():N}", assigneeUserId });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var issueId = created.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync($"/api/issues/{issueId}");
        getResponse.EnsureSuccessStatusCode();
        var issue = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (issueId, issue.GetProperty("rowVersion").GetString()!);
    }

    [Fact]
    public async Task My_tasks_spans_projects_and_excludes_done_and_archived_by_default()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);

        var projectA = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var projectB = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var projectC = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var (issueAId, _) = await CreateAssignedIssueAsync(client, projectA.ProjectId, admin.UserId);
        var (issueBId, issueBRowVersion) = await CreateAssignedIssueAsync(client, projectB.ProjectId, admin.UserId);
        var (issueCId, _) = await CreateAssignedIssueAsync(client, projectC.ProjectId, admin.UserId);

        // Issue B goes to its Board's Done column; Project C is archived.
        var moveResponse = await client.PatchAsJsonAsync(
            $"/api/issues/{issueBId}/move", new { boardColumnId = projectB.DoneColumnId, rowVersion = issueBRowVersion });
        moveResponse.EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/projects/{projectC.ProjectId}/archive", null)).EnsureSuccessStatusCode();

        var defaultResponse = await client.GetAsync("/api/dashboard/my-tasks");
        defaultResponse.EnsureSuccessStatusCode();
        var defaultItems = (await defaultResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(defaultItems, i => i.GetProperty("id").GetGuid() == issueAId);
        Assert.DoesNotContain(defaultItems, i => i.GetProperty("id").GetGuid() == issueBId);
        Assert.DoesNotContain(defaultItems, i => i.GetProperty("id").GetGuid() == issueCId);

        var inclusiveResponse = await client.GetAsync("/api/dashboard/my-tasks?includeDone=true&includeArchived=true");
        inclusiveResponse.EnsureSuccessStatusCode();
        var inclusiveItems = (await inclusiveResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(inclusiveItems, i => i.GetProperty("id").GetGuid() == issueAId);
        Assert.Contains(inclusiveItems, i => i.GetProperty("id").GetGuid() == issueBId);
        Assert.Contains(inclusiveItems, i => i.GetProperty("id").GetGuid() == issueCId);
    }

    [Fact]
    public async Task Activity_from_a_workspace_disappears_once_the_caller_is_removed_from_it()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var member = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        // The member does something that writes an ActivityLogEntry, then reads it back.
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {member.AccessToken}");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var before = await ReadActivityIdsAsync(client);
        Assert.NotEmpty(before);

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {admin.AccessToken}");
        (await client.DeleteAsync($"/api/workspaces/{seeded.WorkspaceId}/members/{member.UserId}")).EnsureSuccessStatusCode();

        // BR-04: the feed is scoped to the Workspaces the caller is currently a member of, so
        // losing membership retracts the history rather than leaving it visible.
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {member.AccessToken}");
        var after = await ReadActivityIdsAsync(client);

        Assert.Empty(after);
    }

    private static async Task<List<Guid>> ReadActivityIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/dashboard/recent-activity");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task My_projects_returns_explicit_memberships_with_their_role()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, owner.AccessToken);

        var response = await client.GetAsync("/api/dashboard/my-projects");
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(seeded.ProjectId, items[0].GetProperty("id").GetGuid());
        Assert.Equal(ProjectRole.ProjectAdmin, items[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task My_projects_omits_a_project_the_caller_only_reaches_via_workspace_admin()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, owner.AccessToken);

        // A second Workspace Admin — full authority over the Project per spec/16-rbac.md BR-02/BR-03,
        // but deliberately never given a ProjectMember row.
        var workspaceAdmin = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {owner.AccessToken}");
        (await client.PostAsJsonAsync(
            $"/api/workspaces/{seeded.WorkspaceId}/invitations",
            new { email = workspaceAdmin.Email, role = WorkspaceRole.Admin })).EnsureSuccessStatusCode();

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            token = await db.Invitations
                .Where(i => i.WorkspaceId == seeded.WorkspaceId && i.Email == workspaceAdmin.Email)
                .Select(i => i.Token)
                .SingleAsync();
        }

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {workspaceAdmin.AccessToken}");
        (await client.PostAsync($"/api/invitations/{token}/accept", null)).EnsureSuccessStatusCode();

        // Sanity check: the Workspace Admin really can view the Project — the omission below is
        // about explicit membership (BR-03), not about lacking access.
        var projectResponse = await client.GetAsync($"/api/projects/{seeded.ProjectId}");
        projectResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/dashboard/my-projects");
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.Empty(items);
    }

    [Fact]
    public async Task Recent_activity_hides_project_scoped_entries_from_a_member_without_that_project()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var plainMember = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, ProjectRole.Viewer);

        var projectScopedEntryId = Guid.NewGuid();
        var workspaceScopedEntryId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

            // Drop the ProjectMember row so this caller is a plain Workspace Member with no Project
            // access at all — the exact situation spec/14-dashboard.md BR-06 guards against.
            db.ProjectMembers.Remove(await db.ProjectMembers.SingleAsync(
                m => m.ProjectId == seeded.ProjectId && m.UserId == plainMember.UserId));

            db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                Id = projectScopedEntryId,
                ActorUserId = admin.UserId,
                WorkspaceId = seeded.WorkspaceId,
                ProjectId = seeded.ProjectId,
                EntityType = "Issue",
                EntityId = Guid.NewGuid(),
                Action = "Created",
                Summary = "created a project-scoped Issue",
                OccurredAtUtc = DateTime.UtcNow
            });
            db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                Id = workspaceScopedEntryId,
                ActorUserId = admin.UserId,
                WorkspaceId = seeded.WorkspaceId,
                ProjectId = null,
                EntityType = "Workspace",
                EntityId = seeded.WorkspaceId,
                Action = "Updated",
                Summary = "renamed the Workspace",
                OccurredAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {plainMember.AccessToken}");
        var response = await client.GetAsync("/api/dashboard/recent-activity?limit=100");
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToList();

        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == projectScopedEntryId);
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == workspaceScopedEntryId);
    }
}
