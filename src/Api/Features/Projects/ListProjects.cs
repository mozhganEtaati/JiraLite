using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md §9 — GET /api/workspaces/{workspaceId}/projects, any Workspace Member.</summary>
public static class ListProjects
{
    public record ProjectItem(Guid Id, string Key, string Name, string? Description, bool IsArchived);

    public record Response(IReadOnlyList<ProjectItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid workspaceId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.Projects
                .Where(p => p.WorkspaceId == workspaceId)
                .OrderBy(p => p.Name)
                .Select(p => new ProjectItem(p.Id, p.Key, p.Name, p.Description, p.IsArchived))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/projects", Handler.Handle)
            .RequireAuthorization("WorkspaceMember")
            .WithTags("Projects");
}
