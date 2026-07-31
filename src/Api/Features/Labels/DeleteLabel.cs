using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Labels;

/// <summary>spec/12-labels.md FR-01, BR-02 (IssueLabel rows cascade at the DB level), BR-05.</summary>
public static class DeleteLabel
{
    public static class Handler
    {
        public static async Task<IResult> Handle(Guid labelId, JiraLiteDbContext db, CancellationToken cancellationToken)
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
                    "Cannot delete a Label in an archived Project.");
            }

            db.Labels.Remove(label);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/labels/{labelId:guid}", Handler.Handle)
            .RequireAuthorization("LabelManage")
            .WithTags("Labels");
}
