using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class ListSubtasksTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ListSubtasksTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_only_the_subtasks_of_the_given_issue()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var otherStoryId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId, title: "Sub 1");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId, title: "Sub 2");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: otherStoryId, title: "Sub for other story");

        var response = await client.GetAsync($"/api/issues/{storyId}/subtasks");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Sub 1", "Sub 2"], titles);
    }
}
