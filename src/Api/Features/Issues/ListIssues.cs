using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>spec/09-issues.md FR-06 — list/filter an entire Project's Issues by type, status (column), assignee, priority, label, or sprint.</summary>
public static class ListIssues
{
    public record IssueItem(Guid Id, string Key, int Number, string Type, string Title, string Priority, Guid BoardColumnId, Guid? SprintId, UserSummary? Assignee, DateOnly? DueDateUtc);

    public record Response(IReadOnlyList<IssueItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            string? type,
            Guid? boardColumnId,
            Guid? assigneeUserId,
            string? priority,
            Guid? labelId,
            Guid? sprintId,
            int? limit,
            string? cursor,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.Issues.Where(i => i.ProjectId == projectId);

            if (!string.IsNullOrEmpty(type)) query = query.Where(i => i.Type == type);
            if (boardColumnId is not null) query = query.Where(i => i.BoardColumnId == boardColumnId);
            if (assigneeUserId is not null) query = query.Where(i => i.AssigneeUserId == assigneeUserId);
            if (!string.IsNullOrEmpty(priority)) query = query.Where(i => i.Priority == priority);
            if (sprintId is not null) query = query.Where(i => i.SprintId == sprintId);
            if (labelId is not null) query = query.Where(i => db.IssueLabels.Any(il => il.IssueId == i.Id && il.LabelId == labelId));

            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);

            var page = await query
                .OrderBy(i => i.Number)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(i => new { i.Id, i.Key, i.Number, i.Type, i.Title, i.Priority, i.BoardColumnId, i.SprintId, i.AssigneeUserId, i.DueDateUtc })
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var pageItems = page.Take(pageSize).ToList();
            var assignees = await db.GetUserSummariesAsync(pageItems.Where(i => i.AssigneeUserId is not null).Select(i => i.AssigneeUserId!.Value), cancellationToken);

            var items = pageItems
                .Select(i => new IssueItem(
                    i.Id, i.Key, i.Number, i.Type, i.Title, i.Priority, i.BoardColumnId, i.SprintId,
                    i.AssigneeUserId is not null && assignees.TryGetValue(i.AssigneeUserId.Value, out var summary) ? summary : null,
                    i.DueDateUtc))
                .ToList();

            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;
            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/issues", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Issues");
}
