using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md §9 — moves the Issue back to the Product Backlog, appended to its bottom; cascades to Subtasks (BR-11).</summary>
public static class RemoveSprintIssue
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, Guid issueId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId && i.SprintId == sprintId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var lastRank = await db.Issues
                .Where(i => i.ProjectId == issue.ProjectId && i.SprintId == null)
                .OrderByDescending(i => i.Rank)
                .Select(i => i.Rank)
                .FirstOrDefaultAsync(cancellationToken);
            var rank = lastRank is null ? LexoRank.Initial() : LexoRank.Next(lastRank);

            issue.SprintId = null;
            issue.Rank = rank;
            issue.UpdatedAtUtc = DateTime.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Issues
                .Where(i => i.ParentIssueId == issue.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, (Guid?)null), cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/sprints/{sprintId:guid}/issues/{issueId:guid}", Handler.Handle)
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
