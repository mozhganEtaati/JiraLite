using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class EditArchiveProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public EditArchiveProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Project_admin_can_edit_name_and_description()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "Old", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync($"/api/projects/{projectId}", new { name = "New Name", description = "New description" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New Name", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Archiving_then_unarchiving_a_project_round_trips_IsArchived()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var archiveResponse = await client.PostAsync($"/api/projects/{projectId}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        var archiveBody = await archiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(archiveBody.GetProperty("isArchived").GetBoolean());

        var unarchiveResponse = await client.PostAsync($"/api/projects/{projectId}/unarchive", null);
        Assert.Equal(HttpStatusCode.OK, unarchiveResponse.StatusCode);
        var unarchiveBody = await unarchiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(unarchiveBody.GetProperty("isArchived").GetBoolean());
    }
}
