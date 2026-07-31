using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md §9, BR-07 — reassigns directly even if already in a different Sprint.
/// spec/07-backlog.md BR-04/BR-06/BR-11 — Subtasks can't be added independently; appended to the
/// bottom of the target Sprint's list; cascades SprintId to the Issue's own Subtasks.
/// </summary>
public static class AddSprintIssue
{
    public record Request(Guid IssueId);

    public record Response(Guid IssueId, Guid SprintId, string Rank);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.IssueId).NotEmpty();
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid sprintId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == request.IssueId, cancellationToken);
            if (issue is null || issue.ProjectId != sprint.ProjectId)
            {
                return Results.BadRequest(new { detail = "issueId must reference an Issue in the same Project as the Sprint." });
            }

            if (issue.Type == IssueType.Subtask)
            {
                return Results.BadRequest(new { detail = "Subtask-type Issues cannot be assigned to a Sprint independently of their parent." });
            }

            var lastRank = await db.Issues
                .Where(i => i.ProjectId == sprint.ProjectId && i.SprintId == sprintId)
                .OrderByDescending(i => i.Rank)
                .Select(i => i.Rank)
                .FirstOrDefaultAsync(cancellationToken);
            var rank = lastRank is null ? LexoRank.Initial() : LexoRank.Next(lastRank);

            issue.SprintId = sprintId;
            issue.Rank = rank;
            issue.UpdatedAtUtc = DateTime.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Issues
                .Where(i => i.ParentIssueId == issue.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, sprintId), cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new Response(issue.Id, sprintId, issue.Rank));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sprints/{sprintId:guid}/issues", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
