using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Boards;

public class BoardTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public BoardTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid ProjectId)> SeedProjectAsync(HttpClient client)
    {
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        return (admin.AccessToken, created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Listing_boards_after_project_creation_returns_the_default_board()
    {
        var client = _factory.CreateClient();
        var (token, projectId) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync($"/api/projects/{projectId}/boards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Project_admin_creates_a_second_board_and_can_get_it_with_its_columns()
    {
        var client = _factory.CreateClient();
        var (token, projectId) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Support", type = "Kanban" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = created.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync($"/api/boards/{boardId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var board = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Support", board.GetProperty("name").GetString());
        Assert.Equal(3, board.GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Creating_a_board_in_an_archived_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var (token, projectId) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await client.PostAsync($"/api/projects/{projectId}/archive", null);

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/boards", new { name = "Support", type = "Kanban" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
