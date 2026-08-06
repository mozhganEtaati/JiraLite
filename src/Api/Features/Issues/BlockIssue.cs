using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Notifications;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// spec/09-issues.md FR-08, BR-15..BR-17 — marks an Issue as blocked with a required reason.
/// Re-blocking an already-blocked Issue rewrites the reason but keeps BlockedSinceUtc, so
/// "blocked for six days" stays true when someone sharpens the wording (BR-16).
/// </summary>
public static class BlockIssue
{
    public record Request(string Reason, string RowVersion);

    public record Response(Guid Id, bool IsBlocked, string? BlockedReason, DateTime? BlockedSinceUtc, string RowVersion);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
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
            NotificationDispatcher notificationDispatcher,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            // BR-17: finished work is not blocked. Allowing it would put a blocker in the sprint
            // report that nobody can act on, because the work it describes is already over.
            var isDone = await db.BoardColumns
                .Where(c => c.Id == issue.BoardColumnId)
                .Select(c => c.IsDoneColumn)
                .SingleAsync(cancellationToken);
            if (isDone)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/issue-already-done",
                    "An Issue in a Done column cannot be blocked. Move it out of Done first.");
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

            var actorUserId = caller.GetUserId();
            var wasBlocked = issue.IsBlocked;

            issue.IsBlocked = true;
            issue.BlockedReason = request.Reason.Trim();
            // Only the first block starts the clock (BR-15).
            issue.BlockedSinceUtc ??= DateTime.UtcNow;
            issue.UpdatedByUserId = actorUserId;
            issue.UpdatedAtUtc = DateTime.UtcNow;

            if (!wasBlocked)
            {
                await IssueBlockNotices.NotifyAsync(
                    db, notificationDispatcher, issue, actorUserId,
                    NotificationType.IssueBlocked, $"{issue.Key} was blocked", "Blocked",
                    $"blocked Issue {issue.Key}", cancellationToken);
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

            return Results.Ok(new Response(
                issue.Id, issue.IsBlocked, issue.BlockedReason, issue.BlockedSinceUtc,
                Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/issues/{issueId:guid}/block", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Issues");
}
