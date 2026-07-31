using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md FR-01, BR-04, BR-08; spec/05-projects.md BR-04 (archived-project write-lock).</summary>
public static class CreateSprint
{
    public record Request(string Name, string? Goal, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public record Response(Guid Id, Guid BoardId, string Name, string? Goal, string Status, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Goal).MaximumLength(500);
            RuleFor(x => x.PlannedEndDateUtc).GreaterThan(x => x.PlannedStartDateUtc)
                .WithMessage("plannedEndDateUtc must be after plannedStartDateUtc.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid boardId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var board = await db.Boards.SingleOrDefaultAsync(b => b.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            if (board.Type != BoardType.Scrum)
            {
                return Results.BadRequest(new { detail = "Sprints can only be created on Scrum-type Boards." });
            }

            var project = await db.Projects.SingleAsync(p => p.Id == board.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot create a Sprint in an archived Project.");
            }

            var sprint = new Sprint
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                ProjectId = board.ProjectId,
                Name = request.Name.Trim(),
                Goal = request.Goal?.Trim(),
                Status = SprintStatus.Planned,
                PlannedStartDateUtc = request.PlannedStartDateUtc,
                PlannedEndDateUtc = request.PlannedEndDateUtc,
                CreatedByUserId = caller.GetUserId(),
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Sprints.Add(sprint);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/sprints/{sprint.Id}",
                new Response(sprint.Id, sprint.BoardId, sprint.Name, sprint.Goal, sprint.Status, sprint.PlannedStartDateUtc, sprint.PlannedEndDateUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/boards/{boardId:guid}/sprints", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("BoardContribute")
            .WithTags("Sprints");
}
