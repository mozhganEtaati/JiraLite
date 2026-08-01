using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Admin;

/// <summary>spec/17-admin.md FR-02, BR-02 — every Workspace member with their Workspace role and per-Project roles.</summary>
public static class ListAdminUsers
{
    public record ProjectRoleItem(Guid ProjectId, string ProjectKey, string Role);

    public record UserItem(
        Guid UserId, string DisplayName, string? AvatarUrl, string Email, bool IsActive,
        string WorkspaceRole, DateTime JoinedAtUtc, IReadOnlyList<ProjectRoleItem> ProjectRoles);

    public record Response(IReadOnlyList<UserItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            int? limit,
            string? cursor,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
            {
                return Results.NotFound();
            }

            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);

            var page = await db.WorkspaceMembers
                .Where(m => m.WorkspaceId == workspaceId)
                .Join(db.UserProfiles, m => m.UserId, p => p.UserId, (m, p) => new { m, p })
                .Join(db.Users, x => x.m.UserId, u => u.Id, (x, u) => new { x.m, x.p, u })
                .OrderBy(x => x.p.DisplayName)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(x => new { x.m.UserId, x.p.DisplayName, x.p.AvatarUrl, x.u.Email, x.u.IsActive, x.m.Role, x.m.CreatedAtUtc })
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var pageItems = page.Take(pageSize).ToList();
            var userIds = pageItems.Select(x => x.UserId).ToList();

            var projectRoles = await db.ProjectMembers
                .Where(m => userIds.Contains(m.UserId))
                .Join(db.Projects.Where(p => p.WorkspaceId == workspaceId), m => m.ProjectId, p => p.Id,
                    (m, p) => new { m.UserId, ProjectId = p.Id, p.Key, m.Role })
                .ToListAsync(cancellationToken);

            var items = pageItems
                .Select(x => new UserItem(
                    x.UserId, x.DisplayName, x.AvatarUrl, x.Email, x.IsActive, x.Role, x.CreatedAtUtc,
                    projectRoles
                        .Where(r => r.UserId == x.UserId)
                        .Select(r => new ProjectRoleItem(r.ProjectId, r.Key, r.Role))
                        .ToList()))
                .ToList();

            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;
            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/admin/users", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Admin");
}
