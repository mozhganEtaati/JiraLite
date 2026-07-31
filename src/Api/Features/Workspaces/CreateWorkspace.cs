using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-02, FR-03 — creator becomes WorkspaceMember with role Admin.</summary>
public static class CreateWorkspace
{
    public record Request(string Name, string? Description);

    public record Response(Guid Id, Guid OrganizationId, string Name, string? Description, bool IsArchived, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(1000);
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid orgId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            if (!await db.Organizations.AnyAsync(o => o.Id == orgId, cancellationToken))
            {
                return Results.NotFound();
            }

            var now = DateTime.UtcNow;
            var userId = caller.GetUserId();

            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                IsArchived = false,
                CreatedByUserId = userId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Workspaces.Add(workspace);

            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                UserId = userId,
                Role = WorkspaceRole.Admin,
                CreatedAtUtc = now
            });

            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/workspaces/{workspace.Id}",
                new Response(workspace.Id, workspace.OrganizationId, workspace.Name, workspace.Description, workspace.IsArchived, workspace.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/organizations/{orgId:guid}/workspaces", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("OrganizationOwner")
            .WithTags("Workspaces");
}
