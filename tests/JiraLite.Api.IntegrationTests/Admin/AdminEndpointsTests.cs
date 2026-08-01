using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Admin;

public class AdminEndpointsTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public AdminEndpointsTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Overview_counts_match_including_archived_projects()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var archivedProjectResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{seeded.WorkspaceId}/projects",
            new { key = "ARC", name = $"Archived-{Guid.NewGuid():N}", description = (string?)null });
        archivedProjectResponse.EnsureSuccessStatusCode();
        var archivedProjectId = (await archivedProjectResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsync($"/api/projects/{archivedProjectId}/archive", null)).EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/workspaces/{seeded.WorkspaceId}/admin/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("memberCount").GetInt32());
        Assert.Equal(2, body.GetProperty("projectCount").GetInt32());
        Assert.Equal(1, body.GetProperty("activeProjectCount").GetInt32());
        Assert.Equal(1, body.GetProperty("archivedProjectCount").GetInt32());
    }

    [Fact]
    public async Task Users_list_only_shows_the_projects_a_member_actually_belongs_to()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var member = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var otherProjectResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{seeded.WorkspaceId}/projects",
            new { key = "OTH", name = $"Other-{Guid.NewGuid():N}", description = (string?)null });
        otherProjectResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/workspaces/{seeded.WorkspaceId}/admin/users?limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        var memberItem = items.Single(i => i.GetProperty("userId").GetGuid() == member.UserId);
        var projectRoles = memberItem.GetProperty("projectRoles").EnumerateArray().ToList();

        Assert.Single(projectRoles);
        Assert.Equal(seeded.ProjectId, projectRoles[0].GetProperty("projectId").GetGuid());
        Assert.Equal("Developer", projectRoles[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Nonadmin_is_rejected_with_403_on_every_admin_endpoint()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);

        var member = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Viewer");
        client.DefaultRequestHeaders.Authorization = new("Bearer", member.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/workspaces/{seeded.WorkspaceId}/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/workspaces/{seeded.WorkspaceId}/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/workspaces/{seeded.WorkspaceId}/admin/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/workspaces/{seeded.WorkspaceId}/admin/roles")).StatusCode);
    }

    [Fact]
    public async Task Role_catalog_is_identical_across_two_different_workspaces()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceA = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var workspaceB = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var responseA = await client.GetAsync($"/api/workspaces/{workspaceA}/admin/roles");
        var responseB = await client.GetAsync($"/api/workspaces/{workspaceB}/admin/roles");

        var bodyA = await responseA.Content.ReadAsStringAsync();
        var bodyB = await responseB.Content.ReadAsStringAsync();

        Assert.Equal(bodyA, bodyB);
    }
}
