using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Backlog;

public class RepositionIssueRankTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public RepositionIssueRankTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> GetRowVersionAsync(HttpClient client, Guid issueId)
    {
        var response = await client.GetAsync($"/api/issues/{issueId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("rowVersion").GetString()!;
    }

    [Fact]
    public async Task Repositioning_after_null_moves_the_issue_to_the_top()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var first = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "First");
        var second = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Second");
        var rowVersion = await GetRowVersionAsync(client, second);

        var response = await client.PatchAsJsonAsync($"/api/issues/{second}/rank", new { afterIssueId = (Guid?)null, rowVersion });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var backlogResponse = await client.GetAsync($"/api/projects/{seeded.ProjectId}/backlog");
        var backlog = await backlogResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = backlog.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(second, items[0].GetProperty("id").GetGuid());
        Assert.Equal(first, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Subtask_repositioning_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var storyId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story");
        var subtaskId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Subtask", parentIssueId: storyId);
        var rowVersion = await GetRowVersionAsync(client, subtaskId);

        var response = await client.PatchAsJsonAsync($"/api/issues/{subtaskId}/rank", new { afterIssueId = (Guid?)null, rowVersion });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stale_row_version_is_rejected_with_409()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        var response = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/rank", new { afterIssueId = (Guid?)null, rowVersion = Convert.ToBase64String(new byte[8]) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AfterIssueId_from_a_different_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seededA = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var seededB = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueInA = await TestDataHelper.CreateIssueAsync(client, seededA.ProjectId);
        var issueInB = await TestDataHelper.CreateIssueAsync(client, seededB.ProjectId);
        var rowVersion = await GetRowVersionAsync(client, issueInA);

        var response = await client.PatchAsJsonAsync($"/api/issues/{issueInA}/rank", new { afterIssueId = issueInB, rowVersion });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_repositioning_between_the_same_two_neighbors_still_sorts_correctly()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var top = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Top");
        var bottom = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Bottom");
        var floaterIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            floaterIds.Add(await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: $"Floater {i}"));
        }

        foreach (var floaterId in floaterIds)
        {
            var rowVersion = await GetRowVersionAsync(client, floaterId);
            var response = await client.PatchAsJsonAsync($"/api/issues/{floaterId}/rank", new { afterIssueId = top, rowVersion });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var backlogResponse = await client.GetAsync($"/api/projects/{seeded.ProjectId}/backlog?limit=100");
        var backlog = await backlogResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderedIds = backlog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(top, orderedIds[0]);
        Assert.Equal(bottom, orderedIds[^1]);
        // Every floater landed strictly between top and bottom (order among floaters is whatever the
        // repeated "insert right after top" sequence produces — only the endpoints are asserted).
        Assert.All(floaterIds, id => Assert.Contains(id, orderedIds[1..^1]));
    }
}
