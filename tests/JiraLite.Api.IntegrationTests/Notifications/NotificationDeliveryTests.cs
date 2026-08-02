using System.Net.Http.Headers;
using System.Net.Http.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Notifications;

/// <summary>
/// spec/13-notifications.md §15 delivery bullets, plus spec/10-comments.md §15 "assignee, reporter
/// and prior commenters are notified" and spec/03-workspaces.md §15 "an email is dispatched".
/// <see cref="NotificationTests"/> covers reading and marking rows; this covers who gets what, and
/// through which channel (task T048).
/// </summary>
public class NotificationDeliveryTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public NotificationDeliveryTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_comment_notifies_the_assignee_the_reporter_and_prior_commenters_but_never_its_author()
    {
        var client = _factory.CreateClient();
        var reporter = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, reporter.AccessToken);
        var assignee = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, reporter.AccessToken, "Developer");
        var priorCommenter = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, reporter.AccessToken, "Developer");
        var author = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, reporter.AccessToken, "Developer");

        // Reported by `reporter` (they create it), assigned to `assignee`.
        Authenticate(client, reporter.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        (await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = assignee.UserId })).EnsureSuccessStatusCode();

        Authenticate(client, priorCommenter.AccessToken);
        (await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "First." })).EnsureSuccessStatusCode();

        await ClearNotificationsAsync();

        Authenticate(client, author.AccessToken);
        (await client.PostAsJsonAsync($"/api/issues/{issueId}/comments", new { body = "Second." })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var recipients = await db.Notifications
            .Where(n => n.Type == NotificationType.CommentAdded && n.EntityId == issueId)
            .Select(n => n.RecipientUserId)
            .ToListAsync();

        Assert.Equal(
            new[] { assignee.UserId, priorCommenter.UserId, reporter.UserId }.Order().ToArray(),
            recipients.Order().ToArray());
        Assert.DoesNotContain(author.UserId, recipients);
    }

    [Fact]
    public async Task A_recipient_with_in_app_off_but_email_on_gets_no_row_and_still_gets_the_email()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var assignee = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        Authenticate(client, assignee.AccessToken);
        (await client.PatchAsJsonAsync("/api/users/me/notification-preferences", new { emailEnabled = true, inAppEnabled = false }))
            .EnsureSuccessStatusCode();

        Authenticate(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        (await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = assignee.UserId })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.Notifications.AnyAsync(n => n.RecipientUserId == assignee.UserId));
        Assert.True(await AnyEmailJobForAsync(assignee.Email, NotificationType.IssueAssigned));
    }

    [Fact]
    public async Task A_recipient_with_both_channels_off_gets_neither_a_row_nor_an_email()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var seeded = await TestDataHelper.CreateProjectAsync(client, admin.AccessToken);
        var assignee = await TestDataHelper.AddProjectMemberAsync(
            client, _factory, seeded.WorkspaceId, seeded.ProjectId, admin.AccessToken, "Developer");

        Authenticate(client, assignee.AccessToken);
        (await client.PatchAsJsonAsync("/api/users/me/notification-preferences", new { emailEnabled = false, inAppEnabled = false }))
            .EnsureSuccessStatusCode();

        Authenticate(client, admin.AccessToken);
        var issueId = await TestDataHelper.CreateIssueAsync(client, seeded.ProjectId);
        (await client.PatchAsJsonAsync($"/api/issues/{issueId}", new { assigneeUserId = assignee.UserId })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.False(await db.Notifications.AnyAsync(n => n.RecipientUserId == assignee.UserId));
        Assert.False(await AnyEmailJobForAsync(assignee.Email, NotificationType.IssueAssigned));
    }

    [Fact]
    public async Task An_invitation_emails_the_invitee_without_creating_a_notification_row()
    {
        var client = _factory.CreateClient();
        var admin = await TestDataHelper.RegisterAndLoginAsync(client);
        var workspaceId = await TestDataHelper.CreateWorkspaceAsync(client, admin.AccessToken);
        // Registered so we can prove no row was created *for an existing user* — BR-06 sends the
        // invitation straight to the address either way.
        var invitee = await TestDataHelper.RegisterAndLoginAsync(client);

        Authenticate(client, admin.AccessToken);
        (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/invitations", new { email = invitee.Email, role = "Member" }))
            .EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        Assert.True(await AnyEmailJobForAsync(invitee.Email, "invited to join"));
        Assert.False(await db.Notifications.AnyAsync(n => n.RecipientUserId == invitee.UserId));
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private async Task ClearNotificationsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Notification]");
#pragma warning restore EF1002
    }

    /// <summary>
    /// Reads Hangfire's own job table. The dispatcher enqueues rather than sending inline, so the
    /// enqueued job is the only observable evidence that the email channel fired — and the test
    /// host's <see cref="NoOpEmailSender"/> is deliberately stateless, so it cannot be asked.
    ///
    /// <paramref name="subjectFragment"/> is not optional in practice: getting a member onto a
    /// Project sends them an invitation email first, so matching on the address alone would report
    /// a notification email that was never sent.
    /// </summary>
    private async Task<bool> AnyEmailJobForAsync(string email, string subjectFragment)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();

        // Arguments is a JSON array of the Execute parameters: [toEmail, subject, body, token].
        const string sql = """
            SELECT COUNT(1)
            FROM [HangFire].[Job]
            WHERE InvocationData LIKE '%SendEmailJob%'
              AND Arguments LIKE @emailPattern
              AND Arguments LIKE @subjectPattern
            """;

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@emailPattern", $"%{email}%");
            command.Parameters.AddWithValue("@subjectPattern", $"%{subjectFragment}%");
            return (int)(await command.ExecuteScalarAsync())! > 0;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
