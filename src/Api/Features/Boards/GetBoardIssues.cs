using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Boards;

/// <summary>spec/06-boards.md §9, §11 — Issues grouped by column, ordered by Rank within each column, Subtasks excluded (spec/07-backlog.md BR-04).</summary>
public static class GetBoardIssues
{
    public record IssueItem(Guid Id, string Key, string Title, string Type, string Priority, UserSummary? Assignee, bool IsBlocked);

    public record ColumnGroup(Guid ColumnId, IReadOnlyList<IssueItem> Issues);

    public record Response(IReadOnlyList<ColumnGroup> Columns);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid boardId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var columnIds = await db.BoardColumns
                .Where(c => c.BoardId == boardId)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (columnIds.Count == 0)
            {
                return Results.NotFound();
            }

            var issues = await db.Issues
                .Where(i => columnIds.Contains(i.BoardColumnId) && i.Type != IssueType.Subtask)
                .OrderBy(i => i.Rank)
                .Select(i => new { i.Id, i.Key, i.Title, i.Type, i.Priority, i.BoardColumnId, i.AssigneeUserId, i.IsBlocked })
                .ToListAsync(cancellationToken);

            var assignees = await db.GetUserSummariesAsync(issues.Where(i => i.AssigneeUserId is not null).Select(i => i.AssigneeUserId!.Value), cancellationToken);

            var columns = columnIds
                .Select(columnId => new ColumnGroup(
                    columnId,
                    issues
                        .Where(i => i.BoardColumnId == columnId)
                        .Select(i => new IssueItem(
                            i.Id, i.Key, i.Title, i.Type, i.Priority,
                            i.AssigneeUserId is not null && assignees.TryGetValue(i.AssigneeUserId.Value, out var summary) ? summary : null,
                            i.IsBlocked))
                        .ToList()))
                .ToList();

            return Results.Ok(new Response(columns));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/boards/{boardId:guid}/issues", Handler.Handle)
            .RequireAuthorization("BoardView")
            .WithTags("Boards");
}
