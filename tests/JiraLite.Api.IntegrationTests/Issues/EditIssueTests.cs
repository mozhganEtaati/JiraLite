using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class EditIssueTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public EditIssueTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Developer_edits_title_priority_and_assignee()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Original");

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}", new { title = "Updated", priority = "High", assigneeUserId = admin.UserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", body.GetProperty("title").GetString());
        Assert.Equal("High", body.GetProperty("priority").GetString());
        Assert.Equal(admin.UserId, body.GetProperty("assignee").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Fields_not_present_in_the_request_are_left_unchanged()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Original");

        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { priority = "Critical" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Original", body.GetProperty("title").GetString());
        Assert.Equal("Critical", body.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task Assignee_must_be_a_project_member()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);

        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = outsider.UserId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Developer_cannot_change_the_reporter()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);
        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { reporterUserId = developer.UserId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Project_admin_can_change_the_reporter_to_another_project_member()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { reporterUserId = developer.UserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(developer.UserId, body.GetProperty("reporterUserId").GetGuid());
    }

    [Fact]
    public async Task Assigning_an_issue_notifies_the_new_assignee_but_not_the_actor()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = developer.UserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var developerNotifications = await db.Notifications.Where(n => n.RecipientUserId == developer.UserId).ToListAsync();
        Assert.Single(developerNotifications);
        Assert.Equal(NotificationType.IssueAssigned, developerNotifications[0].Type);
        Assert.Equal("Issue", developerNotifications[0].EntityType);
        Assert.Equal(issueId, developerNotifications[0].EntityId);

        var adminNotifications = await db.Notifications.Where(n => n.RecipientUserId == admin.UserId).ToListAsync();
        Assert.Empty(adminNotifications);
    }

    [Fact]
    public async Task Reassigning_to_the_same_assignee_does_not_notify_again()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = developer.UserId });
        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = developer.UserId, title = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var developerNotifications = await db.Notifications.Where(n => n.RecipientUserId == developer.UserId).ToListAsync();
        Assert.Single(developerNotifications);
    }

    [Fact]
    public async Task Viewer_is_forbidden_from_editing()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", viewer.AccessToken);
        var response = await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { title = "Nope" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
