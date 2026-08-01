using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Dashboard;

/// <summary>spec/14-dashboard.md FR-01, BR-01, BR-02 — Issues assigned to the caller across Projects they belong to.</summary>
public static class GetMyTasks
{
    public record TaskItem(Guid Id, string Key, string Type, string Title, string Priority, DateOnly? DueDateUtc, string ProjectKey, string ColumnName);

    public record Response(IReadOnlyList<TaskItem> Items, CursorPagination.PageInfo PageInfo);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            bool? includeDone,
            bool? includeArchived,
            int? limit,
            string? cursor,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var query = db.Issues
                .Where(i => i.AssigneeUserId == userId)
                .Where(i => db.ProjectMembers.Any(m => m.ProjectId == i.ProjectId && m.UserId == userId))
                .Join(db.Projects, i => i.ProjectId, p => p.Id, (i, p) => new { Issue = i, Project = p })
                .Join(db.BoardColumns, x => x.Issue.BoardColumnId, c => c.Id, (x, c) => new { x.Issue, x.Project, Column = c });

            if (includeArchived != true)
            {
                query = query.Where(x => !x.Project.IsArchived);
            }

            if (includeDone != true)
            {
                query = query.Where(x => !x.Column.IsDoneColumn);
            }

            var pageSize = Math.Clamp(limit ?? 25, 1, 100);
            var offset = CursorPagination.DecodeOffset(cursor);

            var page = await query
                .OrderBy(x => x.Issue.DueDateUtc)
                .ThenBy(x => x.Issue.Id)
                .Skip(offset)
                .Take(pageSize + 1)
                .Select(x => new TaskItem(
                    x.Issue.Id, x.Issue.Key, x.Issue.Type, x.Issue.Title, x.Issue.Priority,
                    x.Issue.DueDateUtc, x.Project.Key, x.Column.Name))
                .ToListAsync(cancellationToken);

            var hasNextPage = page.Count > pageSize;
            var items = page.Take(pageSize).ToList();
            var nextCursor = hasNextPage ? CursorPagination.EncodeOffset(offset + pageSize) : null;

            return Results.Ok(new Response(items, new CursorPagination.PageInfo(hasNextPage, nextCursor)));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/my-tasks", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Dashboard");
}
