using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
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
