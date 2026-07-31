using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md §9, §14 — ProjectMember (any role) or Workspace Admin.</summary>
public static class GetProject
{
    public record Response(Guid Id, Guid WorkspaceId, string Key, string Name, string? Description, bool IsArchived, DateTime CreatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var response = await db.Projects
                .Where(p => p.Id == projectId)
                .Select(p => new Response(p.Id, p.WorkspaceId, p.Key, p.Name, p.Description, p.IsArchived, p.CreatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            return response is null ? Results.NotFound() : Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Projects");
}
