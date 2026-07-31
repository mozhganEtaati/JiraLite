using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class GetIssueTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public GetIssueTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Project_member_can_get_an_issue_including_subtask_count_and_row_version()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);

        var response = await client.GetAsync($"/api/issues/{storyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("subtaskCount").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("rowVersion").GetString()));
        Assert.Empty(body.GetProperty("labels").EnumerateArray());
    }

    [Fact]
    public async Task Unrelated_user_is_forbidden_from_viewing_the_issue()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);
        var response = await client.GetAsync($"/api/issues/{issueId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nonexistent_issue_returns_404()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);

        var response = await client.GetAsync($"/api/issues/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
