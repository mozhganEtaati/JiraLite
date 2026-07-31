using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Notifications;

/// <summary>spec/13-notifications.md FR-04, §9 — the caller's own Notifications, newest first.</summary>
public static class ListNotifications
{
    public record NotificationItem(Guid Id, string Type, string Summary, string EntityType, Guid EntityId, bool IsRead, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<NotificationItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            int? limit,
            string? cursor,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);
            var userId = caller.GetUserId();

            var page = await db.Notifications
                .Where(n => n.RecipientUserId == userId)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(n => new NotificationItem(n.Id, n.Type, n.Summary, n.EntityType, n.EntityId, n.IsRead, n.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var items = page.Take(pageSize).ToList();
            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;

            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/notifications", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Notifications");
}
