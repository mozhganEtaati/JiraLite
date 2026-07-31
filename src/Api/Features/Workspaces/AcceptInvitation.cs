using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-05, BR-04. Flat route — token is globally unique.</summary>
public static class AcceptInvitation
{
    public record Response(Guid WorkspaceId, string Role);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            string token,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Token == token, cancellationToken);
            if (invitation is null)
            {
                return Results.NotFound();
            }

            if (invitation.Status != InvitationStatus.Pending || invitation.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return ProblemResults.Gone(
                    "https://jiralite.dev/errors/invitation-not-pending",
                    "This invitation is no longer valid.");
            }

            var userId = caller.GetUserId();
            var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
            if (!string.Equals(user.Email.Trim(), invitation.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ProblemResults.Forbidden(
                    "https://jiralite.dev/errors/invitation-email-mismatch",
                    "This invitation was addressed to a different email.");
            }

            var now = DateTime.UtcNow;
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = invitation.WorkspaceId,
                UserId = userId,
                Role = invitation.Role,
                CreatedAtUtc = now
            });

            invitation.Status = InvitationStatus.Accepted;
            invitation.AcceptedAtUtc = now;
            invitation.AcceptedByUserId = userId;

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(invitation.WorkspaceId, invitation.Role));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/invitations/{token}/accept", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Workspaces");
}
