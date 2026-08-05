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

    /// <summary>
    /// spec/19-api-guidelines.md §7 — a member list has to name the people in it. The assertion
    /// above only counts rows, so the endpoint returning a bare UserId went unnoticed until the
    /// assignee picker rendered a list of blank options.
    /// </summary>
    [Fact]
    public async Task Listing_project_members_returns_their_user_summary()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        // CreateProjectAsync is what authenticates the client, so the rename has to follow it.
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        (await client.PatchAsJsonAsync("/api/users/me", new { displayName = "Ada Lovelace" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/projects/{seeded.ProjectId}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var member = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single();

        Assert.Equal(admin.UserId, member.GetProperty("userId").GetGuid());
        Assert.Equal("Ada Lovelace", member.GetProperty("displayName").GetString());
        Assert.Equal("ProjectAdmin", member.GetProperty("role").GetString());
        Assert.True(member.TryGetProperty("avatarUrl", out _));
        // The web client reads joinedAtUtc; createdAtUtc would silently render "Invalid Date".
        Assert.True(member.TryGetProperty("joinedAtUtc", out var joined));
        Assert.NotEqual(default, joined.GetDateTime());
    }
}
