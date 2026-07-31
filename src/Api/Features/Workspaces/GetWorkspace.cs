using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9 — WorkspaceMember (any role).</summary>
public static class GetWorkspace
{
    public record Response(Guid Id, Guid OrganizationId, string Name, string? Description, bool IsArchived, DateTime CreatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var response = await db.Workspaces
                .Where(w => w.Id == workspaceId)
                .Select(w => new Response(w.Id, w.OrganizationId, w.Name, w.Description, w.IsArchived, w.CreatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            return response is null ? Results.NotFound() : Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}", Handler.Handle)
            .RequireAuthorization("WorkspaceMember")
            .WithTags("Workspaces");
}
