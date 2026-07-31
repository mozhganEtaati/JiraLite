using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Labels;

/// <summary>spec/12-labels.md FR-03.</summary>
public static class ListLabels
{
    public record LabelItem(Guid Id, string Name, string Color);

    public record Response(IReadOnlyList<LabelItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.Labels
                .Where(l => l.ProjectId == projectId)
                .OrderBy(l => l.Name)
                .Select(l => new LabelItem(l.Id, l.Name, l.Color))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/labels", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Labels");
}
