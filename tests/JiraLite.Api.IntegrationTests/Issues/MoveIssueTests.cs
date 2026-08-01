using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class MoveIssueTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public MoveIssueTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Moving_to_a_column_on_the_same_board_updates_the_column_and_returns_a_new_row_version()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var getResponse = await client.GetAsync($"/api/issues/{issueId}");
        var issueBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rowVersion = issueBody.GetProperty("rowVersion").GetString()!;

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.DoneColumnId, body.GetProperty("boardColumnId").GetGuid());
        Assert.NotEqual(rowVersion, body.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Stale_row_version_is_rejected_with_409()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion = Convert.ToBase64String(new byte[8]) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Column_from_a_different_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seededA = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var seededB = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seededA.ProjectId);

        var getResponse = await client.GetAsync($"/api/issues/{issueId}");
        var issueBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rowVersion = issueBody.GetProperty("rowVersion").GetString()!;

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move", new { boardColumnId = seededB.DefaultColumnId, rowVersion });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Moving_onto_a_kanban_column_clears_sprint_id_and_cascades_to_subtasks()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var scrumBoardResponse = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/boards", new { name = "Sprint Board", type = "Scrum" });
        scrumBoardResponse.EnsureSuccessStatusCode();
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var scrumBoardId = scrumBoard.GetProperty("id").GetGuid();

        var sprintResponse = await client.PostAsJsonAsync(
            $"/api/boards/{scrumBoardId}/sprints",
            new { name = "Sprint 1", goal = (string?)null, plannedStartDateUtc = "2026-08-01", plannedEndDateUtc = "2026-08-14" });
        sprintResponse.EnsureSuccessStatusCode();
        var sprint = await sprintResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sprintId = sprint.GetProperty("id").GetGuid();

        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);

        // The Sprint-assignment endpoint (spec/08-sprints.md POST /sprints/{id}/issues) doesn't
        // exist until Task 10 — assign directly via DbContext to set up this test's precondition.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            await db.Issues.Where(i => i.Id == storyId || i.Id == subtaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.SprintId, sprintId));
        }

        var getResponse = await client.GetAsync($"/api/issues/{storyId}");
        var issueBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rowVersion = issueBody.GetProperty("rowVersion").GetString()!;
        Assert.Equal(sprintId, issueBody.GetProperty("sprintId").GetGuid());

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{storyId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sprintId").ValueKind);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var subtask = await verifyDb.Issues.SingleAsync(i => i.Id == subtaskId);
        Assert.Null(subtask.SprintId);
    }

    [Fact]
    public async Task Moving_an_issue_notifies_the_assignee_and_reporter_but_not_the_mover()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = developer.UserId });

        var getResponse = await client.GetAsync($"/api/issues/{issueId}");
        var issueBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rowVersion = issueBody.GetProperty("rowVersion").GetString()!;

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        // admin is both the mover and the Issue's reporter (BR-01 self-exclusion applies to both roles).
        var adminNotifications = await db.Notifications.Where(n => n.RecipientUserId == admin.UserId).ToListAsync();
        Assert.Empty(adminNotifications);

        var developerNotifications = await db.Notifications.Where(n => n.RecipientUserId == developer.UserId).ToListAsync();
        Assert.Single(developerNotifications);
        Assert.Equal(NotificationType.IssueStatusChanged, developerNotifications[0].Type);
    }

    [Fact]
    public async Task Moving_to_the_same_column_does_not_notify()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = developer.UserId });

        var getResponse = await client.GetAsync($"/api/issues/{issueId}");
        var issueBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rowVersion = issueBody.GetProperty("rowVersion").GetString()!;

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move", new { boardColumnId = seeded.DefaultColumnId, rowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var developerNotifications = await db.Notifications.Where(n => n.RecipientUserId == developer.UserId).ToListAsync();
        Assert.Empty(developerNotifications);
    }

    [Fact]
    public async Task Viewer_is_forbidden_from_moving_an_issue()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", viewer.AccessToken);
        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move", new { boardColumnId = seeded.DoneColumnId, rowVersion = Convert.ToBase64String(new byte[8]) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
