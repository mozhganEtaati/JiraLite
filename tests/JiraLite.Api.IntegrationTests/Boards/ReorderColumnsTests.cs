using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class ReorderColumnsTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ReorderColumnsTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid BoardId, JiraLite.Api.Common.Domain.BoardColumn[] Columns)> SeedBoardAsync()
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

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var columns = await db.BoardColumns.Where(c => c.BoardId == boardId).OrderBy(c => c.DisplayOrder).ToArrayAsync();
        return (client, boardId, columns);
    }

    [Fact]
    public async Task Valid_reorder_updates_display_order_for_every_column()
    {
        var (client, boardId, columns) = await SeedBoardAsync();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/reorder", new
        {
            columns = new[]
            {
                new { columnId = columns[2].Id, rowVersion = Convert.ToBase64String(columns[2].RowVersion) },
                new { columnId = columns[0].Id, rowVersion = Convert.ToBase64String(columns[0].RowVersion) },
                new { columnId = columns[1].Id, rowVersion = Convert.ToBase64String(columns[1].RowVersion) }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var reordered = await db.BoardColumns.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.DisplayOrder).ToListAsync();
        Assert.Equal(columns[2].Id, reordered[0].Id);
        Assert.Equal(columns[0].Id, reordered[1].Id);
        Assert.Equal(columns[1].Id, reordered[2].Id);
    }

    [Fact]
    public async Task Stale_row_version_is_rejected_with_409()
    {
        var (client, boardId, columns) = await SeedBoardAsync();

        // Change one column first so its RowVersion in the request below is now stale.
        await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/{columns[0].Id}", new { name = "Renamed" });

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/reorder", new
        {
            columns = new[]
            {
                new { columnId = columns[0].Id, rowVersion = Convert.ToBase64String(columns[0].RowVersion) },
                new { columnId = columns[1].Id, rowVersion = Convert.ToBase64String(columns[1].RowVersion) },
                new { columnId = columns[2].Id, rowVersion = Convert.ToBase64String(columns[2].RowVersion) }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Payload_missing_a_column_is_rejected_with_400()
    {
        var (client, boardId, columns) = await SeedBoardAsync();

        var response = await client.PatchAsJsonAsync($"/api/boards/{boardId}/columns/reorder", new
        {
            columns = new[]
            {
                new { columnId = columns[0].Id, rowVersion = Convert.ToBase64String(columns[0].RowVersion) },
                new { columnId = columns[1].Id, rowVersion = Convert.ToBase64String(columns[1].RowVersion) }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
