using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Workspaces;

/// <summary>
/// spec/03-workspaces.md §15 — the membership bullets not covered by
/// <see cref="CreateInvitationTests"/> (invitation creation) or
/// <see cref="RemoveMemberCascadeTests"/> (removal cascade) (task T048).
/// </summary>
public class WorkspaceMembershipTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public WorkspaceMembershipTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creating_a_workspace_makes_the_creator_an_admin_member()
    {
        var client = _factory.CreateClient();
        var creator = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, creator.AccessToken);

        var myRole = await ReadJsonAsync(client, $"/api/workspaces/{workspaceId}/my-role");

        Assert.Equal("Admin", myRole.GetProperty("effectiveRole").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var member = await db.WorkspaceMembers.AsNoTracking()
            .SingleAsync(m => m.WorkspaceId == workspaceId && m.UserId == creator.UserId);
        Assert.Equal(WorkspaceRole.Admin, member.Role);
    }

    [Fact]
    public async Task Accepting_an_invitation_creates_the_membership_with_the_invited_role_and_marks_it_accepted()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var invitee = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, admin.AccessToken);
        var invite = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/invitations", new { email = invitee.Email, role = "Admin" });
        invite.EnsureSuccessStatusCode();
        var token = await ReadInvitationTokenAsync(workspaceId, invitee.Email);

        Authenticate(client, invitee.AccessToken);
        var accept = await client.PostAsync($"/api/invitations/{token}/accept", null);

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var body = await accept.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Admin", body.GetProperty("role").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var member = await db.WorkspaceMembers.AsNoTracking()
            .SingleAsync(m => m.WorkspaceId == workspaceId && m.UserId == invitee.UserId);
        Assert.Equal(WorkspaceRole.Admin, member.Role);

        var invitation = await db.Invitations.AsNoTracking().SingleAsync(i => i.Token == token);
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
    }

    [Fact]
    public async Task An_invitation_cannot_be_accepted_by_a_different_email_than_it_was_addressed_to()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var invitee = await TestDataHelper.RegisterAndLoginAsync(client);
        var stranger = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, admin.AccessToken);
        (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/invitations", new { email = invitee.Email, role = "Member" }))
            .EnsureSuccessStatusCode();
        var token = await ReadInvitationTokenAsync(workspaceId, invitee.Email);

        Authenticate(client, stranger.AccessToken);
        var accept = await client.PostAsync($"/api/invitations/{token}/accept", null);

        Assert.Equal(HttpStatusCode.Forbidden, accept.StatusCode);
    }

    [Fact]
    public async Task The_sole_admin_can_be_neither_removed_nor_demoted()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken, "Member");

        Authenticate(client, admin.AccessToken);
        var demote = await client.PatchAsJsonAsync($"/api/workspaces/{workspaceId}/members/{admin.UserId}", new { role = "Member" });
        var remove = await client.DeleteAsync($"/api/workspaces/{workspaceId}/members/{admin.UserId}");

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
    }

    [Fact]
    public async Task The_sole_admin_cannot_leave_but_can_once_someone_else_is_promoted()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        var member = await InviteAndAcceptAsync(client, workspaceId, admin.AccessToken, "Member");

        Authenticate(client, admin.AccessToken);
        var blockedLeave = await client.PostAsync($"/api/workspaces/{workspaceId}/leave", null);
        Assert.Equal(HttpStatusCode.Conflict, blockedLeave.StatusCode);

        // Same rule as being removed by someone else — promoting a second Admin lifts it.
        (await client.PatchAsJsonAsync($"/api/workspaces/{workspaceId}/members/{member.UserId}", new { role = "Admin" }))
            .EnsureSuccessStatusCode();

        var allowedLeave = await client.PostAsync($"/api/workspaces/{workspaceId}/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, allowedLeave.StatusCode);
    }

    [Fact]
    public async Task A_non_admin_member_can_leave_on_their_own_and_loses_their_project_memberships()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var member = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        Authenticate(client, member.AccessToken);
        var leave = await client.PostAsync($"/api/workspaces/{seeded.WorkspaceId}/leave", null);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == seeded.WorkspaceId && m.UserId == member.UserId));
        Assert.False(await db.ProjectMembers.AnyAsync(m => m.ProjectId == seeded.ProjectId && m.UserId == member.UserId));
    }

    [Fact]
    public async Task Listing_organizations_returns_every_organization_the_caller_owns()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        Authenticate(client, owner.AccessToken);

        var firstId = await CreateOrganizationAsync(client);
        var secondId = await CreateOrganizationAsync(client);

        var body = await ReadJsonAsync(client, "/api/organizations");
        var ids = body.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(firstId, ids);
        Assert.Contains(secondId, ids);
        Assert.Equal(2, ids.Count);
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static async Task<Guid> CreateOrganizationAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/organizations", new { name = $"Org-{Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string uri)
    {
        var response = await client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> ReadInvitationTokenAsync(Guid workspaceId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        return await db.Invitations
            .Where(i => i.WorkspaceId == workspaceId && i.Email == email && i.Status == InvitationStatus.Pending)
            .Select(i => i.Token)
            .SingleAsync();
    }

    /// <summary>Invites a brand-new user at the given role and accepts it as them. Leaves the client unauthenticated.</summary>
    private async Task<TestDataHelper.RegisteredUser> InviteAndAcceptAsync(
        HttpClient client, Guid workspaceId, string adminAccessToken, string role)
    {
        var invitee = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, adminAccessToken);
        (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/invitations", new { email = invitee.Email, role }))
            .EnsureSuccessStatusCode();
        var token = await ReadInvitationTokenAsync(workspaceId, invitee.Email);

        Authenticate(client, invitee.AccessToken);
        (await client.PostAsync($"/api/invitations/{token}/accept", null)).EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;
        return invitee;
    }
}
