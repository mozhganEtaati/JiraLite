using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class StartSprintTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public StartSprintTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid ScrumBoardId)> SeedScrumBoardAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, scrumBoard.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreateSprintAsync(HttpClient client, Guid boardId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/sprints", new
        {
            name,
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Starting_a_planned_sprint_makes_it_active()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId, "Sprint 1");

        var response = await client.PostAsync($"/api/sprints/{sprintId}/start", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("startedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Starting_a_second_sprint_while_one_is_already_active_on_the_same_board_is_rejected()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var firstSprintId = await CreateSprintAsync(client, boardId, "Sprint 1");
        var secondSprintId = await CreateSprintAsync(client, boardId, "Sprint 2");
        await client.PostAsync($"/api/sprints/{firstSprintId}/start", null);

        var response = await client.PostAsync($"/api/sprints/{secondSprintId}/start", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Editing_name_and_goal_of_a_planned_sprint_succeeds()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId, "Sprint 1");

        var response = await client.PatchAsJsonAsync($"/api/sprints/{sprintId}", new
        {
            name = "Sprint 1 - Renamed",
            goal = "New goal",
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-17"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sprint 1 - Renamed", body.GetProperty("name").GetString());
    }
}
