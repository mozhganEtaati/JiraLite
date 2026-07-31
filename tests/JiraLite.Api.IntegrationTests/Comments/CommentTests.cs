using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Comments;

public class CommentTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CommentTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Developer_adds_a_comment_and_it_appears_in_the_list_oldest_first()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var firstResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "First" });
        var secondResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Second" });

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, firstBody.GetProperty("updatedAtUtc").ValueKind);
        Assert.Equal(admin.UserId, firstBody.GetProperty("author").GetProperty("id").GetGuid());
        Assert.True(secondResponse.IsSuccessStatusCode);

        var listResponse = await client.GetAsync($"/api/issues/{issueId}/comments");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = listBody.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("First", items[0].GetProperty("body").GetString());
        Assert.Equal("Second", items[1].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Non_author_cannot_edit_another_users_comment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var commentResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Mine" });
        var comment = await commentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetGuid();

        var otherDeveloper = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");
        client.DefaultRequestHeaders.Authorization = new("Bearer", otherDeveloper.AccessToken);

        var response = await client.PatchAsJsonAsync($"/api/comments/{commentId}", new { body = "Hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Author_edits_their_own_comment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var commentResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Original" });
        var comment = await commentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync($"/api/comments/{commentId}", new { body = "Edited" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Edited", body.GetProperty("body").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Project_admin_can_moderate_delete_another_users_comment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);
        var commentResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "By developer" });
        var comment = await commentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var response = await client.DeleteAsync($"/api/comments/{commentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Author_demoted_to_viewer_can_no_longer_edit_their_own_comment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var developer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);
        var commentResponse = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Before demotion" });
        var comment = await commentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var demoteResponse = await client.PatchAsJsonAsync($"/api/projects/{seeded.ProjectId}/members/{developer.UserId}", new { role = "Viewer" });
        demoteResponse.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new("Bearer", developer.AccessToken);
        var response = await client.PatchAsJsonAsync($"/api/comments/{commentId}", new { body = "Should fail" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_comment_on_an_archived_projects_issue_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var archiveResponse = await client.PostAsync($"/api/projects/{seeded.ProjectId}/archive", null);
        archiveResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Too late" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_add_a_comment()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var viewer = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", viewer.AccessToken);
        var response = await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Nope" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
