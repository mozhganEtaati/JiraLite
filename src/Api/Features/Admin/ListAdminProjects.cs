using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Admin;

/// <summary>spec/17-admin.md FR-03, BR-02 — every Project in the Workspace, including archived, with size stats.</summary>
public static class ListAdminProjects
{
    public record ProjectItem(Guid ProjectId, string Key, string Name, bool IsArchived, int MemberCount, int IssueCount, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<ProjectItem> Items, CursorPagination.PageInfo PageInfo);

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

            var page = await db.Projects
                .Where(p => p.WorkspaceId == workspaceId)
                .OrderBy(p => p.Name)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(p => new ProjectItem(
                    p.Id, p.Key, p.Name, p.IsArchived,
                    db.ProjectMembers.Count(m => m.ProjectId == p.Id),
                    db.Issues.Count(i => i.ProjectId == p.Id),
                    p.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var items = page.Take(pageSize).ToList();
            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;

            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/admin/projects", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Admin");
}
