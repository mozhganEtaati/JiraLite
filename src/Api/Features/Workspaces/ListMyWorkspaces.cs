using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9 — GET /api/workspaces, Workspaces the caller is a member of.</summary>
public static class ListMyWorkspaces
{
    public record WorkspaceItem(Guid Id, Guid OrganizationId, string Name, string? Description, bool IsArchived, string Role);

    public record Response(IReadOnlyList<WorkspaceItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            // Order on the join's raw columns before projecting into the record — ordering by a
            // property of a record constructed in the Join projection does not translate to SQL.
            var items = await db.WorkspaceMembers
                .Where(m => m.UserId == userId)
                .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { m, w })
                .OrderBy(x => x.w.Name)
                .Select(x => new WorkspaceItem(x.w.Id, x.w.OrganizationId, x.w.Name, x.w.Description, x.w.IsArchived, x.m.Role))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Workspaces");
}
