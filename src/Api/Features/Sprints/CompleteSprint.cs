using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>
/// spec/08-sprints.md FR-04, BR-02 — status transition only. BR-05 (carry-forward incomplete
/// Issues to the Product Backlog or another Sprint) is deferred to Phase 4 — no Issue entity
/// exists yet to query/move. carriedForwardIssueCount is always 0 in Phase 3.
/// </summary>
public static class CompleteSprint
{
    public record Response(Guid Id, string Status, DateTime? CompletedAtUtc, int CarriedForwardIssueCount);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid sprintId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            if (sprint.Status != SprintStatus.Active)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/invalid-sprint-transition",
                    "Only an Active Sprint can be completed.");
            }

            sprint.Status = SprintStatus.Completed;
            sprint.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(sprint.Id, sprint.Status, sprint.CompletedAtUtc, CarriedForwardIssueCount: 0));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/sprints/{sprintId:guid}/complete", Handler.Handle)
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
