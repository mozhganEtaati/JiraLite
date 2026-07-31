using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Sprints;

/// <summary>spec/08-sprints.md BR-03 — planned dates editable only while Status = Planned.</summary>
public static class EditSprint
{
    public record Request(string Name, string? Goal, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

    public record Response(Guid Id, string Name, string? Goal, DateOnly PlannedStartDateUtc, DateOnly PlannedEndDateUtc);

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
        public static async Task<IResult> Handle(Guid sprintId, Request request, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var sprint = await db.Sprints.SingleOrDefaultAsync(s => s.Id == sprintId, cancellationToken);
            if (sprint is null)
            {
                return Results.NotFound();
            }

            sprint.Name = request.Name.Trim();
            sprint.Goal = request.Goal?.Trim();

            if (sprint.Status == SprintStatus.Planned)
            {
                sprint.PlannedStartDateUtc = request.PlannedStartDateUtc;
                sprint.PlannedEndDateUtc = request.PlannedEndDateUtc;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(sprint.Id, sprint.Name, sprint.Goal, sprint.PlannedStartDateUtc, sprint.PlannedEndDateUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/sprints/{sprintId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("SprintContribute")
            .WithTags("Sprints");
}
