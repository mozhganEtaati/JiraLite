using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class SprintIssuesTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public SprintIssuesTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(TestDataHelper.SeededProject Seeded, Guid ScrumBoardId, Guid SprintId)> SeedScrumSprintAsync(HttpClient client, string adminAccessToken)
    {
        var seeded = await TestDataHelper.CreateProjectAsync(client, adminAccessToken);

        var boardResponse = await client.PostAsJsonAsync($"/api/projects/{seeded.ProjectId}/boards", new { name = "Sprint Board", type = "Scrum" });
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var scrumBoardId = board.GetProperty("id").GetGuid();

        var sprintResponse = await client.PostAsJsonAsync(
            $"/api/boards/{scrumBoardId}/sprints",
            new { name = "Sprint 1", goal = (string?)null, plannedStartDateUtc = "2026-08-01", plannedEndDateUtc = "2026-08-14" });
        sprintResponse.EnsureSuccessStatusCode();
        var sprint = await sprintResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sprintId = sprint.GetProperty("id").GetGuid();

        return (seeded, scrumBoardId, sprintId);
    }

    [Fact]
    public async Task Adding_an_issue_assigns_it_and_cascades_to_its_subtasks()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var (seeded, _, sprintId) = await SeedScrumSprintAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);

        var response = await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId = storyId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var subtask = await db.Issues.SingleAsync(i => i.Id == subtaskId);
        Assert.Equal(sprintId, subtask.SprintId);
    }

    [Fact]
    public async Task Subtask_cannot_be_added_to_a_sprint_directly()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var (seeded, _, sprintId) = await SeedScrumSprintAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);

        var response = await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId = subtaskId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Removing_an_issue_returns_it_to_the_product_backlog_and_cascades_to_subtasks()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var (seeded, _, sprintId) = await SeedScrumSprintAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);
        (await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId = storyId })).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/sprints/{sprintId}/issues/{storyId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var story = await db.Issues.SingleAsync(i => i.Id == storyId);
        var subtask = await db.Issues.SingleAsync(i => i.Id == subtaskId);
        Assert.Null(story.SprintId);
        Assert.Null(subtask.SprintId);
    }

    [Fact]
    public async Task Completing_a_sprint_carries_forward_incomplete_issues_and_keeps_done_ones()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var (seeded, scrumBoardId, sprintId) = await SeedScrumSprintAsync(client, admin.AccessToken);

        var incompleteId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Incomplete");
        var doneId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Done");
        (await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId = incompleteId })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId = doneId })).EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            var doneColumnId = await db.BoardColumns.Where(c => c.BoardId == scrumBoardId && c.IsDoneColumn).Select(c => c.Id).SingleAsync();
            await db.Issues.Where(i => i.Id == doneId).ExecuteUpdateAsync(s => s.SetProperty(i => i.BoardColumnId, doneColumnId));
        }

        var startResponse = await client.PostAsync($"/api/sprints/{sprintId}/start", null);
        startResponse.EnsureSuccessStatusCode();

        var completeResponse = await client.PostAsJsonAsync($"/api/sprints/{sprintId}/complete", new { moveIncompleteIssuesToSprintId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var body = await completeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("carriedForwardIssueCount").GetInt32());

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var incomplete = await verifyDb.Issues.SingleAsync(i => i.Id == incompleteId);
        var done = await verifyDb.Issues.SingleAsync(i => i.Id == doneId);
        Assert.Null(incomplete.SprintId);
        Assert.Equal(sprintId, done.SprintId);
    }

    [Fact]
    public async Task Deleting_a_planned_sprint_returns_its_issues_to_the_product_backlog()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var (seeded, _, sprintId) = await SeedScrumSprintAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        (await client.PostAsJsonAsync($"/api/sprints/{sprintId}/issues", new { issueId })).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/sprints/{sprintId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var issue = await db.Issues.SingleAsync(i => i.Id == issueId);
        Assert.Null(issue.SprintId);
    }
}
