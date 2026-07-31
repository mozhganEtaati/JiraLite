using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class ColumnTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ColumnTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid BoardId)> SeedBoardAsync()
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
        return (client, boardId);
    }

    [Fact]
    public async Task Adding_a_column_appends_it_after_the_existing_three()
    {
        var (client, boardId) = await SeedBoardAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/columns", new { name = "Code Review", isDefault = false, isDoneColumn = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var getResponse = await client.GetAsync($"/api/boards/{boardId}");
        var board = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, board.GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Deleting_the_last_remaining_column_is_rejected()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var toDoColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "To Do").Select(c => c.Id).SingleAsync();
        var inProgressColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "In Progress").Select(c => c.Id).SingleAsync();
        var doneColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "Done").Select(c => c.Id).SingleAsync();

        // Give "To Do" both flags first, so deleting "In Progress" and "Done" below doesn't
        // trip the BR-02 sole-Default/sole-Done guards — this test isolates BR-01 only.
        await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/{toDoColumnId}", new { isDoneColumn = true });

        var deleteInProgress = await client.DeleteAsync($"/api/boards/{boardId}/columns/{inProgressColumnId}");
        Assert.Equal(HttpStatusCode.OK, deleteInProgress.StatusCode);
        var deleteDone = await client.DeleteAsync($"/api/boards/{boardId}/columns/{doneColumnId}");
        Assert.Equal(HttpStatusCode.OK, deleteDone.StatusCode);

        var lastResponse = await client.DeleteAsync($"/api/boards/{boardId}/columns/{toDoColumnId}");

        Assert.Equal(HttpStatusCode.Conflict, lastResponse.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_sole_default_column_without_another_default_is_rejected()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var toDoColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "To Do").Select(c => c.Id).SingleAsync();

        var response = await client.DeleteAsync($"/api/boards/{boardId}/columns/{toDoColumnId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_sole_done_column_without_another_done_is_rejected()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var doneColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "Done").Select(c => c.Id).SingleAsync();

        var response = await client.DeleteAsync($"/api/boards/{boardId}/columns/{doneColumnId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Setting_a_new_default_column_unsets_the_previous_one()
    {
        var (client, boardId) = await SeedBoardAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var inProgressColumnId = await db.BoardColumns.Where(c => c.BoardId == boardId && c.Name == "In Progress").Select(c => c.Id).SingleAsync();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/{inProgressColumnId}", new { isDefault = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var toDoColumn = await db.BoardColumns.AsNoTracking().SingleAsync(c => c.BoardId == boardId && c.Name == "To Do");
        Assert.False(toDoColumn.IsDefault);
    }

    [Fact]
    public async Task Deleting_a_column_with_an_issue_placed_on_it_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);

        // Move off the sole-default column first so this test isolates the Issue-presence guard
        // from the separate "can't delete the only default column" guard.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var inProgressColumnId = await db.BoardColumns.Where(c => c.BoardId == seeded.BoardId && c.Name == "In Progress").Select(c => c.Id).SingleAsync();

        var getIssueResponse = await client.GetAsync($"/api/issues/{issueId}");
        var issueBody = await getIssueResponse.Content.ReadFromJsonAsync<JsonElement>();
        var moveResponse = await client.PatchAsJsonAsync(
            $"/api/issues/{issueId}/move",
            new { boardColumnId = inProgressColumnId, rowVersion = issueBody.GetProperty("rowVersion").GetString() });
        moveResponse.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/boards/{seeded.BoardId}/columns/{inProgressColumnId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
