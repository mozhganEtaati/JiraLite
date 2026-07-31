using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Projects;

public class ProjectMemberTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public ProjectMemberTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Adding_a_workspace_member_as_a_project_member_then_changing_and_removing_their_role_works()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var teammate = await TestDataHelper.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var inviteResponse = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/invitations", new { email = teammate.Email, role = "Member" });
        Assert.True(inviteResponse.IsSuccessStatusCode);

        // CreateInvitation's response never exposes the raw token (it's only ever delivered by
        // email, per the Phase 5 TODO in CreateInvitation.cs) — read it straight from the DB.
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            token = await db.Invitations
                .Where(i => i.WorkspaceId == workspaceId && i.Email == teammate.Email)
                .Select(i => i.Token)
                .SingleAsync();
        }

        client.DefaultRequestHeaders.Authorization = new("Bearer", teammate.AccessToken);
        var acceptResponse = await client.PostAsync($"/api/invitations/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", admin.AccessToken);
        var addResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/members", new { userId = teammate.UserId, role = "Developer" });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var changeResponse = await client.PatchAsJsonAsync($"/api/projects/{projectId}/members/{teammate.UserId}", new { role = "ProjectAdmin" });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        var removeResponse = await client.DeleteAsync($"/api/projects/{projectId}/members/{teammate.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/projects/{projectId}/members");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(listBody.GetProperty("items").EnumerateArray());
    }
}
