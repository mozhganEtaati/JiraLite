using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-08, BR-09.</summary>
public static class ArchiveWorkspace
{
    public record Response(Guid Id, bool IsArchived, DateTime UpdatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var workspace = await db.Workspaces.SingleOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound();
            }

            workspace.IsArchived = true;
            workspace.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(workspace.Id, workspace.IsArchived, workspace.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/workspaces/{workspaceId:guid}/archive", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
