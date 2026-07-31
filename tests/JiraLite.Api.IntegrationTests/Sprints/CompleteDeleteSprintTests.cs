using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Sprints;

public class CompleteDeleteSprintTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CompleteDeleteSprintTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid BoardId)> SeedScrumBoardAsync()
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

    private static async Task<Guid> CreateSprintAsync(HttpClient client, Guid boardId)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/sprints", new
        {
            name = "Sprint 1",
            goal = (string?)null,
            plannedStartDateUtc = "2026-08-03",
            plannedEndDateUtc = "2026-08-14"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Completing_an_active_sprint_transitions_it_and_sets_completed_at()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId);
        await client.PostAsync($"/api/sprints/{sprintId}/start", null);

        var response = await client.PostAsync($"/api/sprints/{sprintId}/complete", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("completedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Completing_a_sprint_that_is_not_active_is_rejected()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var sprintId = await CreateSprintAsync(client, boardId);

        var response = await client.PostAsync($"/api/sprints/{sprintId}/complete", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_planned_sprint_succeeds_but_deleting_an_active_one_is_rejected()
    {
        var (client, boardId) = await SeedScrumBoardAsync();
        var plannedSprintId = await CreateSprintAsync(client, boardId);
        var deleteResponse = await client.DeleteAsync($"/api/sprints/{plannedSprintId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var activeSprintId = await CreateSprintAsync(client, boardId);
        await client.PostAsync($"/api/sprints/{activeSprintId}/start", null);
        var rejectedResponse = await client.DeleteAsync($"/api/sprints/{activeSprintId}");

        Assert.Equal(HttpStatusCode.Conflict, rejectedResponse.StatusCode);
    }
}
