using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Notifications;

/// <summary>spec/13-notifications.md FR-04, §9.</summary>
public static class GetUnreadCount
{
    public record Response(int UnreadCount);

    public static class Handler
    {
        public static async Task<IResult> Handle(ClaimsPrincipal caller, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var count = await db.Notifications.CountAsync(n => n.RecipientUserId == userId && !n.IsRead, cancellationToken);
            return Results.Ok(new Response(count));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/notifications/unread-count", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Notifications");
}
