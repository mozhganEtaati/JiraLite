using Hangfire;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Notifications;

/// <summary>
/// spec/13-notifications.md BR-01, BR-02, BR-05 — creates a Notification row when the recipient's
/// current NotificationPreference.InAppEnabled allows it, and enqueues an email job when
/// EmailEnabled allows it. Adds the Notification to the caller's own JiraLiteDbContext rather than
/// saving itself, so it commits atomically with whatever entity mutation triggered it.
/// </summary>
public class NotificationDispatcher(JiraLiteDbContext db, IBackgroundJobClient backgroundJobClient)
{
    public async Task NotifyAsync(
        Guid recipientUserId,
        Guid actingUserId,
        string type,
        string summary,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        if (recipientUserId == actingUserId)
        {
            return; // BR-01 — never notify a user of their own action.
        }

        var preference = await db.NotificationPreferences
            .Where(p => p.UserId == recipientUserId)
            .Select(p => new { p.InAppEnabled, p.EmailEnabled })
            .SingleOrDefaultAsync(cancellationToken);

        var inAppEnabled = preference?.InAppEnabled ?? true;
        var emailEnabled = preference?.EmailEnabled ?? true;

        if (inAppEnabled)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                RecipientUserId = recipientUserId,
                Type = type,
                Summary = summary,
                EntityType = entityType,
                EntityId = entityId,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (emailEnabled)
        {
            var recipientEmail = await db.Users
                .Where(u => u.Id == recipientUserId)
                .Select(u => u.Email)
                .SingleAsync(cancellationToken);
            backgroundJobClient.Enqueue<SendEmailJob>(job => job.Execute(recipientEmail, type, summary, CancellationToken.None));
        }
    }
}
