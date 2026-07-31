using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Notifications;

/// <summary>
/// spec/13-notifications.md §9, §13 — scoped to the caller's own Notifications; a Notification
/// belonging to someone else (or not existing) is 404, never 403, to avoid confirming another
/// user's Notification IDs exist. Marking an already-read Notification read again is a no-op 200.
/// </summary>
public static class MarkNotificationRead
{
    public record Response(Guid Id, bool IsRead, DateTime? ReadAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid notificationId,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var notification = await db.Notifications
                .SingleOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId, cancellationToken);
            if (notification is null)
            {
                return Results.NotFound();
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new Response(notification.Id, notification.IsRead, notification.ReadAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/notifications/{notificationId:guid}/read", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Notifications");
}
