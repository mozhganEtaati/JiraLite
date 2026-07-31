using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9, §11 — GET /api/workspaces/{workspaceId}/members.</summary>
public static class ListWorkspaceMembers
{
    public record MemberItem(Guid UserId, string DisplayName, string? AvatarUrl, string Role, DateTime JoinedAtUtc);

    public record Response(IReadOnlyList<MemberItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            // Order on the join's raw columns before projecting into the record — ordering by a
            // property of a record constructed in the Join projection does not translate to SQL.
            var items = await db.WorkspaceMembers
                .Where(m => m.WorkspaceId == workspaceId)
                .Join(db.UserProfiles, m => m.UserId, p => p.UserId, (m, p) => new { m, p })
                .OrderBy(x => x.p.DisplayName)
                .Select(x => new MemberItem(x.m.UserId, x.p.DisplayName, x.p.AvatarUrl, x.m.Role, x.m.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/members", Handler.Handle)
            .RequireAuthorization("WorkspaceMember")
            .WithTags("Workspaces");
}
