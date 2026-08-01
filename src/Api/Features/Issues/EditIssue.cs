using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Notifications;
using JiraLite.Api.Common.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// spec/09-issues.md FR-02, FR-03, BR-12, BR-13, BR-14. Partial update: a field is only changed if
/// present in the request (matching Boards/EditColumn.cs's convention) — Title/Description/
/// DueDateUtc/Estimate/AssigneeUserId cannot be explicitly cleared back to null via this endpoint,
/// only replaced with a new value. ReporterUserId additionally requires IssueManage — a caller who
/// only holds IssueContribute gets 403 if they attempt to change it (BR-13).
/// </summary>
public static class EditIssue
{
    public record Request(string? Title, string? Description, string? Priority, Guid? AssigneeUserId, DateOnly? DueDateUtc, decimal? Estimate, Guid? ReporterUserId);

    public record Response(Guid Id, string Title, string? Description, string Priority, UserSummary? Assignee, DateOnly? DueDateUtc, decimal? Estimate, Guid ReporterUserId, string RowVersion);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(255).When(x => x.Title is not null);
            RuleFor(x => x.Description).MaximumLength(50000);
            RuleFor(x => x.Priority).Must(p => p is null || IssuePriority.All.Contains(p))
                .WithMessage($"Priority must be one of: {string.Join(", ", IssuePriority.All)}.");
            RuleFor(x => x.Estimate).InclusiveBetween(0, 999.99m).When(x => x.Estimate is not null);
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid issueId,
            Request request,
            HttpContext httpContext,
            IAuthorizationService authorizationService,
            JiraLiteDbContext db,
            NotificationDispatcher notificationDispatcher,
            CancellationToken cancellationToken)
        {
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, cancellationToken);
            if (issue is null)
            {
                return Results.NotFound();
            }

            var previousAssigneeUserId = issue.AssigneeUserId;

            if (request.ReporterUserId is not null && request.ReporterUserId != issue.ReporterUserId)
            {
                var manageResult = await authorizationService.AuthorizeAsync(httpContext.User, null, "IssueManage");
                if (!manageResult.Succeeded)
                {
                    return ProblemResults.Forbidden(
                        "https://jiralite.dev/errors/reporter-reassignment-forbidden",
                        "Only a Project Admin or Workspace Admin may change an Issue's Reporter.");
                }

                var reporterIsMember = await db.ProjectMembers.AnyAsync(m => m.ProjectId == issue.ProjectId && m.UserId == request.ReporterUserId, cancellationToken);
                if (!reporterIsMember)
                {
                    return Results.BadRequest(new { detail = "reporterUserId must be a member of this Project." });
                }

                issue.ReporterUserId = request.ReporterUserId.Value;
            }

            if (request.AssigneeUserId is not null)
            {
                var assigneeIsMember = await db.ProjectMembers.AnyAsync(m => m.ProjectId == issue.ProjectId && m.UserId == request.AssigneeUserId, cancellationToken);
                if (!assigneeIsMember)
                {
                    return Results.BadRequest(new { detail = "assigneeUserId must be a member of this Project." });
                }

                issue.AssigneeUserId = request.AssigneeUserId;
            }

            if (request.Title is not null) issue.Title = request.Title.Trim();
            if (request.Description is not null) issue.Description = MarkdownSanitizer.Strip(request.Description);
            if (request.Priority is not null) issue.Priority = request.Priority;
            if (request.DueDateUtc is not null) issue.DueDateUtc = request.DueDateUtc;
            if (request.Estimate is not null) issue.Estimate = request.Estimate;

            var actorUserId = httpContext.User.GetUserId();
            issue.UpdatedByUserId = actorUserId;
            issue.UpdatedAtUtc = DateTime.UtcNow;

            // spec/13-notifications.md FR-01/BR-03: only when the assignee actually changes to a
            // different, non-null user — not on every edit.
            if (issue.AssigneeUserId is not null && issue.AssigneeUserId != previousAssigneeUserId)
            {
                await notificationDispatcher.NotifyAsync(
                    issue.AssigneeUserId.Value,
                    actorUserId,
                    NotificationType.IssueAssigned,
                    $"You were assigned to {issue.Key}",
                    "Issue",
                    issue.Id,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            var assignee = issue.AssigneeUserId is null ? null : await db.GetUserSummaryAsync(issue.AssigneeUserId.Value, cancellationToken);

            return Results.Ok(new Response(
                issue.Id, issue.Title, issue.Description, issue.Priority, assignee,
                issue.DueDateUtc, issue.Estimate, issue.ReporterUserId, Convert.ToBase64String(issue.RowVersion)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/issues/{issueId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("IssueContribute")
            .WithTags("Issues");
}
