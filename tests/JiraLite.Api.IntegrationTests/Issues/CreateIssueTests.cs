using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class CreateIssueTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CreateIssueTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Developer_creates_a_story_and_it_lands_in_the_default_column_with_medium_priority()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues",
            new { type = "Story", title = "Support invitations via email", description = "## AC\n- works" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Story", body.GetProperty("type").GetString());
        Assert.Equal("Medium", body.GetProperty("priority").GetString());
        Assert.Equal(seeded.DefaultColumnId, body.GetProperty("boardColumnId").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sprintId").ValueKind);
        Assert.Equal(admin.UserId, body.GetProperty("reporter").GetProperty("id").GetGuid());
        Assert.StartsWith("JIRA-", body.GetProperty("key").GetString());
        Assert.Equal(1, body.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Sequential_numbers_are_assigned_per_project()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var first = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "First");
        var second = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Second");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var firstIssue = await db.Issues.SingleAsync(i => i.Id == first);
        var secondIssue = await db.Issues.SingleAsync(i => i.Id == second);

        Assert.Equal(1, firstIssue.Number);
        Assert.Equal(2, secondIssue.Number);
        Assert.True(string.CompareOrdinal(secondIssue.Rank, firstIssue.Rank) > 0);
    }

    [Fact]
    public async Task Epic_with_a_parent_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var epicId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Epic");

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues",
            new { type = "Epic", title = "Nested epic", parentIssueId = epicId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subtask_requires_a_story_task_or_bug_parent_not_an_epic()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var epicId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Epic");

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues",
            new { type = "Subtask", title = "Bad subtask", parentIssueId = epicId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subtask_under_a_story_mirrors_the_stated_sprint_id_of_its_parent()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues",
            new { type = "Subtask", title = "Sub work", parentIssueId = storyId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sprintId").ValueKind);
        Assert.Equal(storyId, body.GetProperty("parentIssueId").GetGuid());
    }

    [Fact]
    public async Task Assignee_must_be_a_project_member()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues",
            new { type = "Task", title = "Assigned to outsider", assigneeUserId = outsider.UserId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_is_forbidden_from_creating_an_issue()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", viewer.AccessToken);
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues", new { type = "Task", title = "Viewer's issue" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
