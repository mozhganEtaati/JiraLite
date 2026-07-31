using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-07.</summary>
public static class RevokeInvitation
{
    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            Guid invitationId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var invitation = await db.Invitations
                .SingleOrDefaultAsync(i => i.Id == invitationId && i.WorkspaceId == workspaceId, cancellationToken);
            if (invitation is null)
            {
                return Results.NotFound();
            }

            if (invitation.Status == InvitationStatus.Pending)
            {
                invitation.Status = InvitationStatus.Revoked;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/workspaces/{workspaceId:guid}/invitations/{invitationId:guid}", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
