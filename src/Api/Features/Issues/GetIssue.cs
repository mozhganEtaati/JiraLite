using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>spec/09-issues.md §9, §11.</summary>
public static class GetIssue
{
    public record LabelItem(Guid Id, string Name, string Color);

    public record Response(
        Guid Id, string Key, int Number, string Type, Guid? ParentIssueId, string Title, string? Description,
        string Priority, Guid BoardColumnId, Guid? SprintId, string Rank, UserSummary? Assignee, UserSummary Reporter,
        DateOnly? DueDateUtc, decimal? Estimate, IReadOnlyList<LabelItem> Labels, int SubtaskCount, string RowVersion);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid issueId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var assignee = issue.AssigneeUserId is null ? null : await db.GetUserSummaryAsync(issue.AssigneeUserId.Value, cancellationToken);
            var reporter = (await db.GetUserSummaryAsync(issue.ReporterUserId, cancellationToken))!;

            var labels = await db.IssueLabels
                .Where(il => il.IssueId == issueId)
                .Join(db.Labels, il => il.LabelId, l => l.Id, (il, l) => new LabelItem(l.Id, l.Name, l.Color))
                .ToListAsync(cancellationToken);

            var subtaskCount = await db.Issues.CountAsync(i => i.ParentIssueId == issueId, cancellationToken);

            return Results.Ok(new Response(
                issue.Id, issue.Key, issue.Number, issue.Type, issue.ParentIssueId, issue.Title, issue.Description,
                issue.Priority, issue.BoardColumnId, issue.SprintId, issue.Rank, assignee, reporter,
                issue.DueDateUtc, issue.Estimate, labels, subtaskCount, Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/issues/{issueId:guid}", Handler.Handle)
            .RequireAuthorization("IssueView")
            .WithTags("Issues");
}
