using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-04, BR-02 — no "last ProjectAdmin" guard, Workspace Admin is always a fallback.</summary>
public static class RemoveProjectMember
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Guid userId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.ProjectMembers
                .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            db.ProjectMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/projects/{projectId:guid}/members/{userId:guid}", Handler.Handle)
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
