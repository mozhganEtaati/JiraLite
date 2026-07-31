using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9 — WorkspaceAdmin.</summary>
public static class UpdateWorkspace
{
    public record Request(string Name, string? Description);

    public record Response(Guid Id, string Name, string? Description, DateTime UpdatedAtUtc);

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
            Guid workspaceId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var workspace = await db.Workspaces.SingleOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound();
            }

            workspace.Name = request.Name.Trim();
            workspace.Description = request.Description?.Trim();
            workspace.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(workspace.Id, workspace.Name, workspace.Description, workspace.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/workspaces/{workspaceId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
