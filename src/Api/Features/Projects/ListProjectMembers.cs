using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>
/// spec/05-projects.md §9, §14. Carries the UserSummary fields (spec/19-api-guidelines.md §7)
/// like every other endpoint that surfaces a person — a bare UserId gives a caller no way to
/// name the member, which left the assignee pickers rendering blank rows.
/// Shape matches ListWorkspaceMembers deliberately; both feed the same UI components.
/// </summary>
public static class ListProjectMembers
{
    public record MemberItem(Guid UserId, string DisplayName, string? AvatarUrl, string Role, DateTime JoinedAtUtc);

    public record Response(IReadOnlyList<MemberItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            // Order on the join's raw columns before projecting into the record — ordering by a
            // property of a record constructed in the Join projection does not translate to SQL.
            var items = await db.ProjectMembers
                .Where(m => m.ProjectId == projectId)
                .Join(db.UserProfiles, m => m.UserId, p => p.UserId, (m, p) => new { m, p })
                .OrderBy(x => x.p.DisplayName)
                .Select(x => new MemberItem(x.m.UserId, x.p.DisplayName, x.p.AvatarUrl, x.m.Role, x.m.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/members", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Projects");
}
