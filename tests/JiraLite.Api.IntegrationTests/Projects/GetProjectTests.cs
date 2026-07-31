using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class GetProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public GetProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid WorkspaceId, Guid ProjectId, string AdminToken)> SeedProjectAsync(HttpClient client)
    {
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null });
        var project = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (workspaceId, project.GetProperty("id").GetGuid(), admin.AccessToken);
    }

    [Fact]
    public async Task Project_admin_can_get_the_project_they_created()
    {
        var client = _factory.CreateClient();
        var (_, projectId, token) = await SeedProjectAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unrelated_user_is_forbidden_from_viewing_the_project()
    {
        var client = _factory.CreateClient();
        var (_, projectId, _) = await SeedProjectAsync(client);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);

        var response = await client.GetAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task My_role_endpoint_returns_null_effective_role_for_a_user_with_no_access()
    {
        var client = _factory.CreateClient();
        var (_, projectId, _) = await SeedProjectAsync(client);
        var outsider = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", outsider.AccessToken);

        var response = await client.GetAsync($"/api/projects/{projectId}/my-role");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("effectiveRole").ValueKind);
    }
}
