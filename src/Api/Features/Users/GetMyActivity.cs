using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>
/// spec/02-users.md FR-05, NFR-02; spec/19-api-guidelines.md §5 — cursor-paginated, newest first.
/// Deferred from Phase 1 (spec/21-roadmap.md Phase 1 note) — ActivityLogEntry needed Project to exist.
/// </summary>
public static class GetMyActivity
{
    public record ActivityItem(Guid Id, string EntityType, Guid EntityId, string Action, string Summary, DateTime OccurredAtUtc);

    public record Response(IReadOnlyList<ActivityItem> Items, CursorPagination.PageInfo PageInfo);

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

            var page = await db.ActivityLogEntries
                .Where(e => e.ActorUserId == userId)
                .OrderByDescending(e => e.OccurredAtUtc)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(e => new ActivityItem(e.Id, e.EntityType, e.EntityId, e.Action, e.Summary, e.OccurredAtUtc))
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var items = page.Take(pageSize).ToList();
            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;

            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me/activity", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Users");
}
