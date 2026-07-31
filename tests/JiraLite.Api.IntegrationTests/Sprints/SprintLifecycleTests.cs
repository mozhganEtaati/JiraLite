using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class SprintLifecycleTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public SprintLifecycleTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid ScrumBoardId, Guid KanbanBoardId)> SeedProjectAsync()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var kanbanBoardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var kanbanBoards = await kanbanBoardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var kanbanBoardId = kanbanBoards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, scrumBoard.GetProperty("id").GetGuid(), kanbanBoardId);
    }

    [Fact]
    public async Task Creating_a_sprint_on_a_scrum_board_succeeds_and_it_is_listed_and_gettable()
    {
        var (client, scrumBoardId, _) = await SeedProjectAsync();

        var createResponse = await client.PostAsJsonAsync($"/api/boards/{scrumBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = "Ship the thing",
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var sprint = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sprintId = sprint.GetProperty("id").GetGuid();
        Assert.Equal("Planned", sprint.GetProperty("status").GetString());

        var listResponse = await client.GetAsync($"/api/boards/{scrumBoardId}/sprints");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(list.GetProperty("items").EnumerateArray());

        var getResponse = await client.GetAsync($"/api/sprints/{sprintId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Creating_a_sprint_on_a_kanban_board_is_rejected()
    {
        var (client, _, kanbanBoardId) = await SeedProjectAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{kanbanBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task End_date_not_after_start_date_is_rejected()
    {
        var (client, scrumBoardId, _) = await SeedProjectAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{scrumBoardId}/sprints", new
        {
            name = "Sprint 1",
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-14",
            plannedEndDateUtc = "2026-08-03"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
