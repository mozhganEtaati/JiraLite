using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Notifications;
using JiraLite.Api.Common.Text;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Comments;

/// <summary>spec/10-comments.md FR-01, BR-04 (archived-Project write-lock), NFR-01.</summary>
public static class AddComment
{
    public record Request(string Body);

    public record Response(Guid Id, Guid IssueId, UserSummary Author, string Body, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(10000);
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
            var issue = await db.Issues
                .Where(i => i.Id == issueId)
                .Select(i => new { i.ProjectId, i.Key, i.AssigneeUserId, i.ReporterUserId })
                .SingleOrDefaultAsync(cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var project = await db.Projects.SingleAsync(p => p.Id == issue.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot add a Comment on an Issue in an archived Project.");
            }

            var priorCommenterIds = await db.Comments
                .Where(c => c.IssueId == issueId)
                .Select(c => c.AuthorUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var userId = caller.GetUserId();
            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                IssueId = issueId,
                AuthorUserId = userId,
                Body = MarkdownSanitizer.Strip(request.Body.Trim())!,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = null
            };
            db.Comments.Add(comment);

            var author = (await db.GetUserSummaryAsync(userId, cancellationToken))!;

            // spec/13-notifications.md FR-03: assignee, reporter, and prior commenters, excluding the author.
            var recipients = new HashSet<Guid>(priorCommenterIds) { issue.ReporterUserId };
            if (issue.AssigneeUserId is not null)
            {
                recipients.Add(issue.AssigneeUserId.Value);
            }

            foreach (var recipientUserId in recipients)
            {
                await notificationDispatcher.NotifyAsync(
                    recipientUserId,
                    userId,
                    NotificationType.CommentAdded,
                    $"{author.DisplayName} commented on {issue.Key}",
                    "Issue",
                    issueId,
                    cancellationToken);
            }

            // spec/02-users.md BR-05/BR-06 — a representative Activity entry for Phase 4 (named
            // explicitly as an example handler in BR-05).
            db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                Id = Guid.NewGuid(),
                ActorUserId = userId,
                WorkspaceId = project.WorkspaceId,
                ProjectId = issue.ProjectId,
                EntityType = "Issue",
                EntityId = issueId,
                Action = "Commented",
                Summary = $"commented on Issue {issue.Key}",
                OccurredAtUtc = comment.CreatedAtUtc
            });

            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/comments/{comment.Id}",
                new Response(comment.Id, comment.IssueId, author, comment.Body, comment.CreatedAtUtc, comment.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/issues/{issueId:guid}/comments", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Comments");
}
