using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Notifications;

public class NotificationTests : IClassFixture<JiraLiteApiFactory>, IAsyncLifetime
{
    private readonly JiraLiteApiFactory _factory;

    public NotificationTests(JiraLiteApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await DatabaseResetHelper.ResetAsync(scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedNotificationAsync(Guid recipientUserId, bool isRead = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            Type = NotificationType.CommentAdded,
            Summary = "Jane commented on JIRA-1",
            EntityType = "Issue",
            EntityId = Guid.NewGuid(),
            IsRead = isRead,
            CreatedAtUtc = DateTime.UtcNow,
            ReadAtUtc = isRead ? DateTime.UtcNow : null
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    [Fact]
    public async Task Lists_only_the_callers_own_notifications_newest_first()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var otherUser = await TestDataHelper.RegisterAndLoginAsync(client);
        await SeedNotificationAsync(otherUser.UserId);
        var firstId = await SeedNotificationAsync(user.UserId);
        await Task.Delay(10);
        var secondId = await SeedNotificationAsync(user.UserId);

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var response = await client.GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(secondId, items[0].GetProperty("id").GetGuid());
        Assert.Equal(firstId, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Unread_count_reflects_only_unread_notifications()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        await SeedNotificationAsync(user.UserId, isRead: false);
        await SeedNotificationAsync(user.UserId, isRead: true);

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var response = await client.GetAsync("/api/notifications/unread-count");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Marking_a_notification_read_sets_is_read_and_read_at()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var notificationId = await SeedNotificationAsync(user.UserId);

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var response = await client.PatchAsync($"/api/notifications/{notificationId}/read", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isRead").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("readAtUtc").ValueKind);
    }

    [Fact]
    public async Task Marking_an_already_read_notification_is_idempotent()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var notificationId = await SeedNotificationAsync(user.UserId, isRead: true);

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var response = await client.PatchAsync($"/api/notifications/{notificationId}/read", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_notification_returns_404_not_403()
    {
        var client = _factory.CreateClient();
        var owner = await TestDataHelper.RegisterAndLoginAsync(client);
        var intruder = await TestDataHelper.RegisterAndLoginAsync(client);
        var notificationId = await SeedNotificationAsync(owner.UserId);

        client.DefaultRequestHeaders.Authorization = new("Bearer", intruder.AccessToken);
        var response = await client.PatchAsync($"/api/notifications/{notificationId}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Mark_all_read_marks_every_unread_notification_for_the_caller_only()
    {
        var client = _factory.CreateClient();
        var user = await TestDataHelper.RegisterAndLoginAsync(client);
        var otherUser = await TestDataHelper.RegisterAndLoginAsync(client);
        await SeedNotificationAsync(user.UserId);
        await SeedNotificationAsync(user.UserId);
        var otherNotificationId = await SeedNotificationAsync(otherUser.UserId);

        client.DefaultRequestHeaders.Authorization = new("Bearer", user.AccessToken);
        var response = await client.PostAsync("/api/notifications/read-all", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("markedCount").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JiraLiteDbContext>();
        var otherNotification = await db.Notifications.SingleAsync(n => n.Id == otherNotificationId);
        Assert.False(otherNotification.IsRead);
    }
}
