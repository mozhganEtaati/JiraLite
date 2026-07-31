using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// spec/09-issues.md FR-04, BR-10, BR-11. Moving onto a Kanban Board's column clears SprintId
/// (BR-10) and cascades that clear to the moved Issue's Subtasks (BR-11) in the same transaction —
/// ExecuteUpdateAsync runs immediately rather than at SaveChanges, so both the subtask clear and
/// the concurrency-checked Issue update must commit or roll back together.
/// </summary>
public static class MoveIssue
{
    public record Request(Guid BoardColumnId, string RowVersion);

    public record Response(Guid Id, Guid BoardColumnId, Guid? SprintId, string RowVersion);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.BoardColumnId).NotEmpty();
            RuleFor(x => x.RowVersion).NotEmpty();
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid issueId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var targetColumn = await db.BoardColumns
                .Where(c => c.Id == request.BoardColumnId)
                .Join(db.Boards, c => c.BoardId, b => b.Id, (c, b) => new { Column = c, Board = b })
                .SingleOrDefaultAsync(cancellationToken);
            if (targetColumn is null || targetColumn.Board.ProjectId != issue.ProjectId)
            {
                return Results.BadRequest(new { detail = "boardColumnId must belong to a Board in the same Project as the Issue." });
            }

            byte[] originalRowVersion;
            try
            {
                originalRowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { detail = "rowVersion is not valid base64." });
            }

            db.Entry(issue).Property(i => i.RowVersion).OriginalValue = originalRowVersion;

            issue.BoardColumnId = targetColumn.Column.Id;
            issue.UpdatedByUserId = caller.GetUserId();
            issue.UpdatedAtUtc = DateTime.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (targetColumn.Board.Type == BoardType.Kanban)
            {
                issue.SprintId = null;
                await db.Issues
                    .Where(i => i.ParentIssueId == issue.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.SprintId, (Guid?)null), cancellationToken);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/concurrency-conflict",
                    "This Issue was modified since you last loaded it. Reload and try again.");
            }

            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new Response(issue.Id, issue.BoardColumnId, issue.SprintId, Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/issues/{issueId:guid}/move", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Issues");
}
