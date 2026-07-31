using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md FR-06, BR-06 — Planned only. No Issues to return to the Product Backlog yet (Phase 4).</summary>
public static class DeleteSprint
{
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
                    "https://jiralite.dev/errors/sprint-not-planned",
                    "Only a Planned Sprint can be deleted.");
            }

            db.Sprints.Remove(sprint);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/sprints/{sprintId:guid}", Handler.Handle)
            .RequireAuthorization("SprintManage")
            .WithTags("Sprints");
}
