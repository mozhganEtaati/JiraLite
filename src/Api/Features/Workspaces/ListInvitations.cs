using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9 — GET /api/workspaces/{workspaceId}/invitations (pending only).</summary>
public static class ListInvitations
{
    public record InvitationItem(Guid Id, string Email, string Role, string Status, DateTime ExpiresAtUtc, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<InvitationItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var items = await db.Invitations
                .Where(i => i.WorkspaceId == workspaceId && i.Status == InvitationStatus.Pending)
                .OrderByDescending(i => i.CreatedAtUtc)
                .Select(i => new InvitationItem(i.Id, i.Email, i.Role, i.Status, i.ExpiresAtUtc, i.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/invitations", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
