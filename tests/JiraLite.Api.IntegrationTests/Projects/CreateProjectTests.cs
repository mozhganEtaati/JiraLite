using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class CreateProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CreateProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Workspace_admin_creates_a_project_and_gets_a_default_board_with_three_columns()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/projects",
            new { key = "JIRA", name = "JiraLite Platform", description = "Core work" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = body.GetProperty("id").GetGuid();
        Assert.Equal("JIRA", body.GetProperty("key").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var creatorMembership = await db.ProjectMembers.SingleAsync(m => m.ProjectId == projectId && m.UserId == admin.UserId);
        Assert.Equal(ProjectRole.ProjectAdmin, creatorMembership.Role);

        var board = await db.Boards.SingleAsync(b => b.ProjectId == projectId);
        Assert.Equal("Main Board", board.Name);
        Assert.Equal(BoardType.Kanban, board.Type);
        var columns = await db.BoardColumns.Where(c => c.BoardId == board.Id).OrderBy(c => c.DisplayOrder).ToListAsync();
        Assert.Equal(3, columns.Count);
        Assert.True(columns[0].IsDefault);
        Assert.True(columns[2].IsDoneColumn);
    }

    [Fact]
    public async Task Duplicate_key_within_the_same_workspace_is_rejected_case_insensitively()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null });

        var response = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "jira", name = "P2", description = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
