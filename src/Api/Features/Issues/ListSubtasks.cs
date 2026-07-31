using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>spec/09-issues.md FR-07, §9 — GET /api/issues/{issueId}/subtasks.</summary>
public static class ListSubtasks
{
    public record SubtaskItem(Guid Id, string Key, int Number, string Title, string Priority, Guid BoardColumnId, UserSummary? Assignee);

    public record Response(IReadOnlyList<SubtaskItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid issueId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var subtasks = await db.Issues
                .Where(i => i.ParentIssueId == issueId)
                .OrderBy(i => i.Number)
                .Select(i => new { i.Id, i.Key, i.Number, i.Title, i.Priority, i.BoardColumnId, i.AssigneeUserId })
                .ToListAsync(cancellationToken);

            var assignees = await db.GetUserSummariesAsync(subtasks.Where(s => s.AssigneeUserId is not null).Select(s => s.AssigneeUserId!.Value), cancellationToken);

            var items = subtasks
                .Select(s => new SubtaskItem(
                    s.Id, s.Key, s.Number, s.Title, s.Priority, s.BoardColumnId,
                    s.AssigneeUserId is not null && assignees.TryGetValue(s.AssigneeUserId.Value, out var summary) ? summary : null))
                .ToList();

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/issues/{issueId:guid}/subtasks", Handler.Handle)
            .RequireAuthorization("IssueView")
            .WithTags("Issues");
}
