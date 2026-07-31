using System.Security.Claims;
using System.Security.Cryptography;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-04, BR-05, BR-06, BR-07, BR-09.</summary>
public static class CreateInvitation
{
    public record Request(string Email, string Role);

    public record Response(Guid Id, string Email, string Role, string Status, DateTime ExpiresAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Role).NotEmpty().Must(r => WorkspaceRole.All.Contains(r))
                .WithMessage($"Role must be one of: {string.Join(", ", WorkspaceRole.All)}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            IOptions<InvitationOptions> invitationOptions,
            CancellationToken cancellationToken)
        {
            var workspace = await db.Workspaces.SingleOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound();
            }
            if (workspace.IsArchived)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/workspace-archived",
                    "This Workspace is archived and read-only.");
            }

            var normalizedEmail = request.Email.Trim();

            var alreadyMember = await db.WorkspaceMembers
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.WorkspaceId, u.Email })
                .AnyAsync(x => x.WorkspaceId == workspaceId && x.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);
            if (alreadyMember)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/already-a-member",
                    "This email is already an active member of the Workspace.");
            }

            // BR-06: a new invitation to an email with an existing Pending one revokes the old one.
            var existingPending = await db.Invitations.SingleOrDefaultAsync(
                i => i.WorkspaceId == workspaceId && i.Email.ToLower() == normalizedEmail.ToLower() && i.Status == InvitationStatus.Pending,
                cancellationToken);
            if (existingPending is not null)
            {
                existingPending.Status = InvitationStatus.Revoked;
            }

            var now = DateTime.UtcNow;
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var invitation = new Invitation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Email = normalizedEmail,
                Role = request.Role,
                Token = token,
                Status = InvitationStatus.Pending,
                InvitedByUserId = caller.GetUserId(),
                ExpiresAtUtc = now.AddDays(invitationOptions.Value.ExpiryDays),
                CreatedAtUtc = now
            };
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(cancellationToken);

            // TODO(Phase 5, T040): dispatch the invitation email via Hangfire once notifications exist.

            return Results.Created(
                $"/api/workspaces/{workspaceId}/invitations",
                new Response(invitation.Id, invitation.Email, invitation.Role, invitation.Status, invitation.ExpiresAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/workspaces/{workspaceId:guid}/invitations", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Workspaces");
}
