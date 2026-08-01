using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Dashboard;

/// <summary>
/// spec/14-dashboard.md FR-03, BR-04, BR-06 — ActivityLogEntry rows across the caller's Workspaces,
/// filtered so a Project-scoped entry never surfaces to someone who couldn't otherwise view that Project.
/// </summary>
public static class GetRecentActivity
{
    public record ActivityItem(Guid Id, UserSummary Actor, string Summary, string EntityType, Guid EntityId, DateTime OccurredAtUtc);

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
            var userId = caller.GetUserId();

            var memberWorkspaceIds = await db.WorkspaceMembers
                .Where(m => m.UserId == userId)
                .Select(m => m.WorkspaceId)
                .ToListAsync(cancellationToken);

            var adminWorkspaceIds = await db.WorkspaceMembers
                .Where(m => m.UserId == userId && m.Role == WorkspaceRole.Admin)
                .Select(m => m.WorkspaceId)
                .ToListAsync(cancellationToken);

            var query = db.ActivityLogEntries
                .Where(e => memberWorkspaceIds.Contains(e.WorkspaceId))
                .Where(e => e.ProjectId == null
                    || adminWorkspaceIds.Contains(e.WorkspaceId)
                    || db.ProjectMembers.Any(m => m.ProjectId == e.ProjectId && m.UserId == userId));

            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);

            var page = await query
                .OrderByDescending(e => e.OccurredAtUtc)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(e => new { e.Id, e.ActorUserId, e.Summary, e.EntityType, e.EntityId, e.OccurredAtUtc })
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var pageItems = page.Take(pageSize).ToList();
            var actors = await db.GetUserSummariesAsync(pageItems.Select(x => x.ActorUserId), cancellationToken);

            var items = pageItems
                .Select(x => new ActivityItem(x.Id, actors[x.ActorUserId], x.Summary, x.EntityType, x.EntityId, x.OccurredAtUtc))
                .ToList();

            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;
            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/recent-activity", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Dashboard");
}
