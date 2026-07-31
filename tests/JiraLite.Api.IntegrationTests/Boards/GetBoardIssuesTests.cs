using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class GetBoardIssuesTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public GetBoardIssuesTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Groups_issues_by_column_and_excludes_subtasks()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story", title: "In default column");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId, title: "Hidden subtask");

        var response = await client.GetAsync($"/api/boards/{seeded.BoardId}/issues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var columns = body.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(3, columns.Count);

        var defaultColumn = columns.Single(c => c.GetProperty("columnId").GetGuid() == seeded.DefaultColumnId);
        var issuesInDefault = defaultColumn.GetProperty("issues").EnumerateArray().ToList();
        Assert.Single(issuesInDefault);
        Assert.Equal(storyId, issuesInDefault[0].GetProperty("id").GetGuid());

        var doneColumn = columns.Single(c => c.GetProperty("columnId").GetGuid() == seeded.DoneColumnId);
        Assert.Empty(doneColumn.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task Nonexistent_board_returns_404()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);

        var response = await client.GetAsync($"/api/boards/{Guid.NewGuid()}/issues");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
