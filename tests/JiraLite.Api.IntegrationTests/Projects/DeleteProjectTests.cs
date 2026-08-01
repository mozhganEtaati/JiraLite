using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class DeleteProjectTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public DeleteProjectTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_non_archived_project_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_archived_project_cascades_and_detaches_activity_log_entries()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        // No activity entry is seeded here: CreateProject already writes a Project/"Created" entry
        // carrying this ProjectId, which is exactly what the detach assertion below needs. Seeding a
        // second one made the lookup ambiguous.
        await client.PostAsync($"/api/projects/{projectId}/archive", null);
        var response = await client.DeleteAsync($"/api/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await verifyDb.Projects.AnyAsync(p => p.Id == projectId));
        Assert.False(await verifyDb.Boards.AnyAsync(b => b.ProjectId == projectId));
        // Assert over every matching entry rather than a single one, so adding another Project-scoped
        // activity write later strengthens this test instead of breaking it.
        var activityEntries = await verifyDb.ActivityLogEntries
            .Where(e => e.EntityId == projectId && e.EntityType == "Project")
            .ToListAsync();
        Assert.NotEmpty(activityEntries);
        Assert.All(activityEntries, entry =>
        {
            Assert.Null(entry.ProjectId);
            Assert.Equal(workspaceId, entry.WorkspaceId);
        });
    }
}
