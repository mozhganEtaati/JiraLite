using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Workspaces;

public class RemoveMemberCascadeTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public RemoveMemberCascadeTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Removing_a_workspace_member_also_removes_their_project_memberships_in_that_workspace()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var created = await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/projects", new { key = "JIRA", name = "P1", description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();

        var teammate = await TestDataHelper.RegisterAndLoginAsync(client);
        Guid teammateProjectMemberId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
            db.WorkspaceMembers.Add(new() { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = teammate.UserId, Role = "Member", CreatedAtUtc = DateTime.UtcNow });
            var member = new JiraLite.Api.Common.Domain.ProjectMember { Id = Guid.NewGuid(), ProjectId = projectId, UserId = teammate.UserId, Role = "Developer", CreatedAtUtc = DateTime.UtcNow };
            db.ProjectMembers.Add(member);
            await db.SaveChangesAsync();
            teammateProjectMemberId = member.Id;
        }

        await client.DeleteAsync($"/api/workspaces/{workspaceId}/members/{teammate.UserId}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await verifyDb.ProjectMembers.AnyAsync(m => m.Id == teammateProjectMemberId));
    }
}
