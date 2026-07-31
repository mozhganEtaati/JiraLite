using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md FR-02, BR-01, BR-02, NFR-01. The application-level check below rejects the
/// common case with a clean 409; the filtered unique index from Task 2
/// (IX_Sprint_BoardId_ActiveOnly) is the actual source of atomicity under concurrent start calls
/// — a race that wins the check-then-update gap still fails at SaveChangesAsync and is caught here.
/// </summary>
public static class StartSprint
{
    public record Response(Guid Id, string Status, DateTime? StartedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Planned)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/invalid-sprint-transition",
                    "Only a Planned Sprint can be started.");
            }

            var anotherActiveExists = await db.Sprints.AnyAsync(
                s => s.BoardId == sprint.BoardId && s.Id != sprintId && s.Status == SprintStatus.Active,
                cancellationToken);
            if (anotherActiveExists)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/board-already-has-active-sprint",
                    "This Board already has an Active Sprint.");
            }

            sprint.Status = SprintStatus.Active;
            sprint.StartedAtUtc = DateTime.UtcNow;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/board-already-has-active-sprint",
                    "This Board already has an Active Sprint.");
            }

            return Results.Ok(new Response(sprint.Id, sprint.Status, sprint.StartedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sprints/{sprintId:guid}/start", Handler.Handle)
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
