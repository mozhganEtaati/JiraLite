using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-04, BR-01 — only existing WorkspaceMembers may be added.</summary>
public static class AddProjectMember
{
    public record Request(Guid UserId, string Role);

    public record Response(Guid UserId, string Role, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.UserId).NotEmpty();
            RuleFor(x => x.Role).NotEmpty().Must(r => ProjectRole.All.Contains(r))
                .WithMessage($"Role must be one of: {string.Join(", ", ProjectRole.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            var isWorkspaceMember = await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == project.WorkspaceId && m.UserId == request.UserId, cancellationToken);
            if (!isWorkspaceMember)
            {
                return Results.BadRequest(new { detail = "User must be a member of the owning Workspace before being added to a Project." });
            }

            if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == request.UserId, cancellationToken))
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/already-project-member",
                    "This user is already a member of the Project.");
            }

            var now = DateTime.UtcNow;
            var member = new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = request.UserId,
                Role = request.Role,
                CreatedAtUtc = now
            };
            db.ProjectMembers.Add(member);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/projects/{projectId}/members/{member.UserId}",
                new Response(member.UserId, member.Role, member.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/projects/{projectId:guid}/members", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
