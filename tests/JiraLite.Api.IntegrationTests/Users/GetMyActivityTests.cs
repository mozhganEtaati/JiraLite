using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Users;

public class GetMyActivityTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public GetMyActivityTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Returns_empty_page_for_a_user_with_no_activity()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);

        var response = await client.GetAsync("/api/users/me/activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
    }

    [Fact]
    public async Task Paginates_activity_newest_first_and_the_second_page_follows_the_cursor()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, user.AccessToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.ActivityLogEntries.Add(new ActivityLogEntry
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = user.UserId,
                    WorkspaceId = workspaceId,
                    ProjectId = null,
                    EntityType = "Workspace",
                    EntityId = workspaceId,
                    Action = "Created",
                    Summary = $"did thing {i}",
                    OccurredAtUtc = DateTime.UtcNow.AddMinutes(i)
                });
            }
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var firstPageResponse = await client.GetAsync("/api/users/me/activity?limit=2");
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstItems = firstPage.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, firstItems.Count);
        Assert.Equal("did thing 2", firstItems[0].GetProperty("summary").GetString());
        Assert.True(firstPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        var cursor = firstPage.GetProperty("pageInfo").GetProperty("nextCursor").GetString();

        var secondPageResponse = await client.GetAsync($"/api/users/me/activity?limit=2&cursor={Uri.EscapeDataString(cursor!)}");
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondItems = secondPage.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(secondItems);
        Assert.Equal("did thing 0", secondItems[0].GetProperty("summary").GetString());
        Assert.False(secondPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
    }

    [Fact]
    public async Task Creating_a_workspace_project_issue_and_comment_each_write_a_real_activity_entry()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "First comment" });

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var response = await client.GetAsync("/api/users/me/activity?limit=100");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "Workspace" && i.GetProperty("action").GetString() == "Created");
        Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "Project" && i.GetProperty("action").GetString() == "Created");
        Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "Issue" && i.GetProperty("action").GetString() == "Created" && i.GetProperty("entityId").GetGuid() == issueId);
        Assert.Contains(items, i => i.GetProperty("entityType").GetString() == "Issue" && i.GetProperty("action").GetString() == "Commented" && i.GetProperty("entityId").GetGuid() == issueId);
    }
}
