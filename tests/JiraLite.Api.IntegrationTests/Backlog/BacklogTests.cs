using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Backlog;

public class BacklogTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public BacklogTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Product_backlog_returns_issues_ordered_by_rank_excluding_subtasks()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var first = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "First");
        var second = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Second");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: first, title: "A Subtask");

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/backlog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(first, items[0].GetProperty("id").GetGuid());
        Assert.Equal(second, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Nonexistent_sprint_backlog_returns_404()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);

        var response = await client.GetAsync($"/api/sprints/{Guid.NewGuid()}/backlog");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
