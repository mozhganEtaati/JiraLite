using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md FR-04. No "last ProjectAdmin" guard — spec/05-projects.md BR-02: Workspace Admin is always a fallback authority.</summary>
public static class ChangeProjectMemberRole
{
    public record Request(string Role);

    public record Response(Guid UserId, string Role);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Role).NotEmpty().Must(r => ProjectRole.All.Contains(r))
                .WithMessage($"Role must be one of: {string.Join(", ", ProjectRole.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid projectId,
            Guid userId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var member = await db.ProjectMembers
                .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);
            if (member is null)
            {
                return Results.NotFound();
            }

            member.Role = request.Role;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(member.UserId, member.Role));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/projects/{projectId:guid}/members/{userId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("ProjectManage")
            .WithTags("Projects");
}
