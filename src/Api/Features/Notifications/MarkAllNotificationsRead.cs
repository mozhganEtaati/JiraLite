using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Notifications;

/// <summary>spec/13-notifications.md §9.</summary>
public static class MarkAllNotificationsRead
{
    public record Response(int MarkedCount);

    public static class Handler
    {
        public static async Task<IResult> Handle(ClaimsPrincipal caller, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var now = DateTime.UtcNow;

            var markedCount = await db.Notifications
                .Where(n => n.RecipientUserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAtUtc, now), cancellationToken);

            return Results.Ok(new Response(markedCount));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/notifications/read-all", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Notifications");
}
