using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Workspaces;

public class CreateInvitationTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public CreateInvitationTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Admin_invites_a_new_email_and_a_pending_invitation_is_created()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/invitations", new { email = "new.teammate@example.com", role = "Member" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("new.teammate@example.com", body.GetProperty("email").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Inviting_an_email_that_is_already_an_active_member_is_rejected()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/invitations", new { email = admin.Email, role = "Member" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_second_invitation_to_the_same_email_revokes_the_first_pending_one()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/invitations", new { email = "teammate@example.com", role = "Member" });
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = firstBody.GetProperty("id").GetGuid();

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/invitations", new { email = "teammate@example.com", role = "Admin" });

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var firstInvitation = await db.Invitations.SingleAsync(i => i.Id == firstId);
        Assert.Equal(InvitationStatus.Revoked, firstInvitation.Status);
    }
}
