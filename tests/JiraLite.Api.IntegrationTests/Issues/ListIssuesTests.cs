using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Issues;

public class ListIssuesTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ListIssuesTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lists_all_issues_in_a_project()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Bug", title: "A bug");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story", title: "A story");

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/issues");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Filters_by_type()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Bug", title: "A bug");
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, type: "Story", title: "A story");

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/issues?type=Bug");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Bug", items[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Filters_by_assignee()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId, title: "Unassigned");

        var assignedResponse = await client.PostAsJsonAsync(
            $"/api/projects/{seeded.ProjectId}/issues",
            new { type = "Task", title = "Assigned to admin", assigneeUserId = admin.UserId });
        assignedResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/issues?assigneeUserId={admin.UserId}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Assigned to admin", items[0].GetProperty("title").GetString());
        Assert.Equal(admin.UserId, items[0].GetProperty("assignee").GetProperty("id").GetGuid());
    }
}
