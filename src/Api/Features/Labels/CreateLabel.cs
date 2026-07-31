using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Labels;

/// <summary>spec/12-labels.md FR-01, BR-01, BR-05.</summary>
public static class CreateLabel
{
    public record Request(string Name, string Color);

    public record Response(Guid Id, Guid ProjectId, string Name, string Color, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Color).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$")
                .WithMessage("Color must be a hex value in the form #RRGGBB.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Request request,
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
                    "Cannot create a Label in an archived Project.");
            }

            var name = request.Name.Trim();
            if (await db.Labels.AnyAsync(l => l.ProjectId == projectId && l.Name.ToLower() == name.ToLower(), cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/duplicate-label-name",
                    $"A Label named '{name}' already exists in this Project.");
            }

            var label = new Label
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = name,
                Color = request.Color,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Labels.Add(label);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/labels/{label.Id}",
                new Response(label.Id, label.ProjectId, label.Name, label.Color, label.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/labels", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Labels");
}
