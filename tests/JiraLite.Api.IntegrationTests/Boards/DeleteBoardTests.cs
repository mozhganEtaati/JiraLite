using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class DeleteBoardTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DeleteBoardTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_the_only_board_in_a_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/boards/{boardId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_a_board_updates_its_name()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        var boardsResponse = await client.GetAsync($"/api/projects/{projectId}/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = boards.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}", new { name = "Renamed Board" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed Board", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_board_with_a_completed_sprint_referencing_it_cannot_be_deleted_even_with_zero_issues()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var scrumBoardResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Scrum", type = "Scrum" });
        var scrumBoard = await scrumBoardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var scrumBoardId = scrumBoard.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            db.Sprints.Add(new Sprint
            {
                Id = Guid.NewGuid(),
                BoardId = scrumBoardId,
                ProjectId = projectId,
                Name = "Sprint 1",
                Status = SprintStatus.Completed,
                PlannedStartDateUtc = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)),
                PlannedEndDateUtc = DateOnly.FromDateTime(DateTime.UtcNow),
                StartedAtUtc = DateTime.UtcNow.AddDays(-14),
                CompletedAtUtc = DateTime.UtcNow,
                CreatedByUserId = admin.UserId,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-14)
            });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/boards/{scrumBoardId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
