using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Common.Auth;

/// <summary>Caller is any ProjectMember of the Attachment's Issue's Project (resolved via "attachmentId"), or Workspace Admin. spec/11-attachments.md §14 — download/preview.</summary>
public class AttachmentViewRequirement : IAuthorizationRequirement;

/// <summary>(Uploader + currently Developer/ProjectAdmin, per BR-07) OR ProjectAdmin (moderation) OR Workspace Admin. spec/11-attachments.md §14.</summary>
public class AttachmentDeleteRequirement : IAuthorizationRequirement;

file static class AttachmentAuthorizationQueries
{
    public static async Task<(Guid UploadedByUserId, Guid ProjectId, Guid WorkspaceId)?> ResolveAsync(JiraLiteDbContext db, Guid attachmentId) =>
        await db.Attachments
            .Where(a => a.Id == attachmentId)
            .Join(db.Issues, a => a.IssueId, i => i.Id, (a, i) => new { a.UploadedByUserId, i.ProjectId })
            .Join(db.Projects, x => x.ProjectId, p => p.Id, (x, p) => new { x.UploadedByUserId, p.Id, p.WorkspaceId })
            .Select(x => new ValueTuple<Guid, Guid, Guid>(x.UploadedByUserId, x.Id, x.WorkspaceId))
            .Cast<(Guid, Guid, Guid)?>()
            .SingleOrDefaultAsync();

    public static Task<bool> IsWorkspaceAdminAsync(JiraLiteDbContext db, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Admin);

    public static Task<string?> GetProjectRoleAsync(JiraLiteDbContext db, Guid projectId, Guid userId) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId && m.UserId == userId).Select(m => (string?)m.Role).SingleOrDefaultAsync();
}

public class AttachmentViewAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<AttachmentViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AttachmentViewRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var attachmentId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "attachmentId");
        if (attachmentId is null) return;

        var resolved = await AttachmentAuthorizationQueries.ResolveAsync(db, attachmentId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await AttachmentAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }
        if (await AttachmentAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId) is not null) context.Succeed(requirement);
    }
}

public class AttachmentDeleteAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    JiraLiteDbContext db) : AuthorizationHandler<AttachmentDeleteRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AttachmentDeleteRequirement requirement)
    {
        if (!context.User.TryGetUserId(out var userId)) return;
        var attachmentId = RouteValueHelper.GetGuidRouteValue(httpContextAccessor.HttpContext, "attachmentId");
        if (attachmentId is null) return;

        var resolved = await AttachmentAuthorizationQueries.ResolveAsync(db, attachmentId.Value);
        if (resolved is null) { context.Succeed(requirement); return; }

        if (await AttachmentAuthorizationQueries.IsWorkspaceAdminAsync(db, resolved.Value.WorkspaceId, userId)) { context.Succeed(requirement); return; }

        var role = await AttachmentAuthorizationQueries.GetProjectRoleAsync(db, resolved.Value.ProjectId, userId);
        if (role == ProjectRole.ProjectAdmin) { context.Succeed(requirement); return; }
        if (resolved.Value.UploadedByUserId == userId && role == ProjectRole.Developer) context.Succeed(requirement);
    }
}
