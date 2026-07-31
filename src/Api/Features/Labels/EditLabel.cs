using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Labels;

/// <summary>spec/12-labels.md FR-01, BR-01, BR-05. Partial update: only fields present are changed.</summary>
public static class EditLabel
{
    public record Request(string? Name, string? Color);

    public record Response(Guid Id, string Name, string Color);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).MaximumLength(50).When(x => x.Name is not null);
            RuleFor(x => x.Color).Matches("^#[0-9A-Fa-f]{6}$").When(x => x.Color is not null)
                .WithMessage("Color must be a hex value in the form #RRGGBB.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid labelId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var label = await db.Labels.SingleOrDefaultAsync(l => l.Id == labelId, cancellationToken);
            if (label is null)
            {
                return Results.NotFound();
            }

            var project = await db.Projects.SingleAsync(p => p.Id == label.ProjectId, cancellationToken);
            if (project.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/project-archived",
                    "Cannot edit a Label in an archived Project.");
            }

            if (request.Name is not null)
            {
                var name = request.Name.Trim();
                var duplicateExists = await db.Labels.AnyAsync(
                    l => l.ProjectId == label.ProjectId && l.Id != labelId && l.Name.ToLower() == name.ToLower(), cancellationToken);
                if (duplicateExists)
                {
                    return ProblemResults.Conflict(
                        "https://jiralite.dev/errors/duplicate-label-name",
                        $"A Label named '{name}' already exists in this Project.");
                }

                label.Name = name;
            }

            if (request.Color is not null)
            {
                label.Color = request.Color;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(label.Id, label.Name, label.Color));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/labels/{labelId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("LabelManage")
            .WithTags("Labels");
}
