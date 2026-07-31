using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-05. Flat route — token is globally unique.</summary>
public static class DeclineInvitation
{
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

            invitation.Status = InvitationStatus.Declined;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/invitations/{token}/decline", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Workspaces");
}
