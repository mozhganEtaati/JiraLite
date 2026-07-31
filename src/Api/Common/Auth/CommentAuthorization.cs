using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>
/// Comment's AuthorUserId matches the caller AND the caller currently holds ProjectMember.Role in
/// (Developer, ProjectAdmin) — authorship alone does not entitle editing (spec/10-comments.md BR-07).
/// A WorkspaceAdmin who is not the author still cannot edit (edit is author-only).
/// </summary>
public class CommentEditRequirement : IAuthorizationRequirement;

/// <summary>
/// (Author + currently Developer/ProjectAdmin, per BR-07) OR ProjectAdmin (moderation) OR Workspace
/// Admin. spec/10-comments.md §14.
/// </summary>
public class CommentDeleteRequirement : IAuthorizationRequirement;

file static class CommentAuthorizationQueries
{
    public static async Task<(Guid AuthorUserId, Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid commentId) =>
        await db.Comments
            .Where(c => c.Id == commentId)
            .Join(db.Issues, c => c.IssueId, i => i.Id, (c, i) => new { c.AuthorUserId, i.ProjectId })
            .Join(db.Projects, x => x.ProjectId, p => p.Id, (x, p) => new { x.AuthorUserId, p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid, Guid>(x.AuthorUserId, x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class CommentEditAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<CommentEditRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CommentEditRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var commentId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "commentId");
        if (commentId is null) return;

        var resolved = await CommentAuthorizationQueries.ResolveAsync(db, commentId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (resolved.Value.AuthorUserId != userId) return;

        var role = await CommentAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role is ProjectRole.Developer or ProjectRole.ProjectAdmin) context.Succeed(requirement);
    }
}

public class CommentDeleteAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<CommentDeleteRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CommentDeleteRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var commentId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "commentId");
        if (commentId is null) return;

        var resolved = await CommentAuthorizationQueries.ResolveAsync(db, commentId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await CommentAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }

        var role = await CommentAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role == ProjectRole.ProjectAdmin) { context.Succeed(requirement); return; }
        if (resolved.Value.AuthorUserId == userId && role == ProjectRole.Developer) context.Succeed(requirement);
    }
}
