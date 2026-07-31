using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Backlog;

/// <summary>spec/07-backlog.md FR-01 — SprintId IS NULL, excludes Subtasks, ordered by Rank ascending.</summary>
public static class GetProductBacklog
{
    public record BacklogItem(Guid Id, string Key, string Title, string Type, string Priority, string Rank, UserSummary? Assignee);

    public record Response(IReadOnlyList<BacklogItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            int? limit,
            string? cursor,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(limit ?? 50, 1, 200);
            var offset = CursorPagination.DecodeOffset(cursor);

            var page = await db.Issues
                .Where(i => i.ProjectId == projectId && i.SprintId == null && i.Type != IssueType.Subtask)
                .OrderBy(i => i.Rank)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(i => new { i.Id, i.Key, i.Title, i.Type, i.Priority, i.Rank, i.AssigneeUserId })
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var pageItems = page.Take(pageSize).ToList();
            var assignees = await db.GetUserSummariesAsync(pageItems.Where(i => i.AssigneeUserId is not null).Select(i => i.AssigneeUserId!.Value), cancellationToken);

            var items = pageItems
                .Select(i => new BacklogItem(
                    i.Id, i.Key, i.Title, i.Type, i.Priority, i.Rank,
                    i.AssigneeUserId is not null && assignees.TryGetValue(i.AssigneeUserId.Value, out var summary) ? summary : null))
                .ToList();

            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;
            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/backlog", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Backlog");
}
