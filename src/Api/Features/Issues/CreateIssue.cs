using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Ranking;
using JiraLite.Api.Common.Text;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Issues;

/// <summary>
/// spec/09-issues.md FR-01, BR-01–BR-04 (hierarchy), BR-07 (Number/Key), BR-08 (default column),
/// BR-11 (Subtask SprintId mirrors parent), BR-12 (Assignee must be a ProjectMember),
/// BR-13 (Reporter defaults to creator), BR-14 (Priority defaults to Medium); spec/07-backlog.md
/// BR-06 (appended to the bottom of its target list).
/// </summary>
public static class CreateIssue
{
    public record Request(
        string Type,
        string Title,
        string? Description,
        string? Priority,
        Guid? ParentIssueId,
        Guid? AssigneeUserId,
        DateOnly? DueDateUtc,
        decimal? Estimate);

    public record Response(
        Guid Id,
        string Key,
        int Number,
        string Type,
        Guid? ParentIssueId,
        string Title,
        string Priority,
        Guid BoardColumnId,
        Guid? SprintId,
        UserSummary? Assignee,
        UserSummary Reporter,
        DateOnly? DueDateUtc,
        decimal? Estimate,
        DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Type).NotEmpty().Must(t => IssueType.All.Contains(t))
                .WithMessage($"Type must be one of: {string.Join(", ", IssueType.All)}.");
            RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
            RuleFor(x => x.Description).MaximumLength(50000);
            RuleFor(x => x.Priority).Must(p => p is null || IssuePriority.All.Contains(p))
                .WithMessage($"Priority must be one of: {string.Join(", ", IssuePriority.All)}.");
            RuleFor(x => x.Estimate).InclusiveBetween(0, 999.99m).When(x => x.Estimate is not null);
        }
    }

    public static class Handler
    {
        private const int MaxNumberAssignmentAttempts = 3;

        public static async Task<IResult> Handle(
            Guid projectId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot create an Issue in an archived Project.");
            }

            string? parentType = null;
            Guid? parentSprintId = null;
            if (request.ParentIssueId is not null)
            {
                var parent = await db.Issues
                    .Where(i => i.Id == request.ParentIssueId && i.ProjectId == projectId)
                    .Select(i => new { i.Type, i.SprintId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (parent is null)
                {
                    return Results.BadRequest(new { detail = "parentIssueId does not reference an Issue in this Project." });
                }

                parentType = parent.Type;
                parentSprintId = parent.SprintId;
            }

            var hierarchyError = IssueHierarchyRules.Validate(request.Type, request.ParentIssueId, parentType);
            if (hierarchyError is not null)
            {
                return Results.BadRequest(new { detail = hierarchyError });
            }

            var userId = caller.GetUserId();
            if (request.AssigneeUserId is not null &&
                !await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == request.AssigneeUserId, cancellationToken))
            {
                return Results.BadRequest(new { detail = "assigneeUserId must be a member of this Project." });
            }

            // spec/06-boards.md BR-07 — "the Project's default Board" has no explicit flag; the
            // bootstrap Board created alongside the Project (CreateProject.cs) is always the one
            // with the lowest DisplayOrder, so that's the stable definition used here.
            var defaultBoard = await db.Boards
                .Where(b => b.ProjectId == projectId)
                .OrderBy(b => b.DisplayOrder)
                .FirstAsync(cancellationToken);
            var defaultColumnId = await db.BoardColumns
                .Where(c => c.BoardId == defaultBoard.Id && c.IsDefault)
                .Select(c => c.Id)
                .SingleAsync(cancellationToken);

            // BR-11: a Subtask's SprintId always mirrors its parent's, from creation onward.
            var sprintId = request.Type == IssueType.Subtask ? parentSprintId : null;

            var lastRank = await db.Issues
                .Where(i => i.ProjectId == projectId && i.SprintId == sprintId)
                .OrderByDescending(i => i.Rank)
                .Select(i => i.Rank)
                .FirstOrDefaultAsync(cancellationToken);
            var rank = lastRank is null ? LexoRank.Initial() : LexoRank.Next(lastRank);

            var now = DateTime.UtcNow;
            Issue issue;

            for (var attempt = 1; ; attempt++)
            {
                var nextNumber = 1 + (await db.Issues
                    .Where(i => i.ProjectId == projectId)
                    .Select(i => (int?)i.Number)
                    .MaxAsync(cancellationToken) ?? 0);

                issue = new Issue
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Number = nextNumber,
                    Key = $"{project.Key}-{nextNumber}",
                    Type = request.Type,
                    ParentIssueId = request.ParentIssueId,
                    Title = request.Title.Trim(),
                    Description = MarkdownSanitizer.Strip(request.Description),
                    Priority = request.Priority ?? IssuePriority.Medium,
                    BoardColumnId = defaultColumnId,
                    SprintId = sprintId,
                    Rank = rank,
                    AssigneeUserId = request.AssigneeUserId,
                    ReporterUserId = userId,
                    DueDateUtc = request.DueDateUtc,
                    Estimate = request.Estimate,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now,
                    UpdatedByUserId = userId,
                    UpdatedAtUtc = now
                };

                db.Issues.Add(issue);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateException) when (attempt < MaxNumberAssignmentAttempts)
                {
                    db.Entry(issue).State = EntityState.Detached;
                }
            }

            var reporter = (await db.GetUserSummaryAsync(userId, cancellationToken))!;
            var assignee = request.AssigneeUserId is null ? null : await db.GetUserSummaryAsync(request.AssigneeUserId.Value, cancellationToken);

            return Results.Created(
                $"/api/issues/{issue.Id}",
                new Response(
                    issue.Id, issue.Key, issue.Number, issue.Type, issue.ParentIssueId, issue.Title, issue.Priority,
                    issue.BoardColumnId, issue.SprintId, assignee, reporter, issue.DueDateUtc, issue.Estimate, issue.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/issues", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectContribute")
            .WithTags("Issues");
}
