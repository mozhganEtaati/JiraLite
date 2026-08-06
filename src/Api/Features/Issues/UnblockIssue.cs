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
/// spec/09-issues.md FR-09, BR-17 — clears the blocked state and its reason. Unblocking an Issue
/// that is not blocked is a 409 rather than a silent no-op, matching how the Sprint lifecycle
/// treats transitions that have already happened (spec/08-sprints.md §13).
/// </summary>
public static class UnblockIssue
{
    public record Request(string RowVersion);

    public record Response(Guid Id, bool IsBlocked, string RowVersion);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.RowVersion).NotEmpty();
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

            if (!issue.IsBlocked)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/issue-not-blocked",
                    "This Issue is not blocked.");
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

            issue.IsBlocked = false;
            issue.BlockedReason = null;
            issue.BlockedSinceUtc = null;
            issue.UpdatedByUserId = actorUserId;
            issue.UpdatedAtUtc = DateTime.UtcNow;

            await IssueBlockNotices.NotifyAsync(
                db, notificationDispatcher, issue, actorUserId,
                NotificationType.IssueUnblocked, $"{issue.Key} was unblocked", "Unblocked",
                $"unblocked Issue {issue.Key}", cancellationToken);

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

            return Results.Ok(new Response(issue.Id, issue.IsBlocked, Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/issues/{issueId:guid}/unblock", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Issues");
}
