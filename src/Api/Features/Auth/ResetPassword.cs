using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Auth;

/// <summary>
/// spec/01-authentication.md FR-07, BR-11, BR-12.
///
/// Redeems a one-time token for a new password. Every rejection — unknown, already used, expired,
/// or owned by a deactivated account — returns the same body, for the same reason login does: the
/// caller here is unauthenticated, so a specific answer tells them which tokens exist.
/// </summary>
public static class ResetPassword
{
    public record Request(string Token, string NewPassword);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Token).NotEmpty();
            // Same rules as Register — a reset must not be a way to set a password the registration
            // form would have refused.
            RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
                .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
                .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
        }
    }

    public static class Handler
    {
        private static IResult InvalidToken() =>
            ProblemResults.BadRequest(
                "https://jiralite.dev/errors/invalid-password-reset-token",
                "This password reset link is invalid or has expired. Request a new one.");

        public static async Task<IResult> Handle(
            Request request,
            JiraLiteDbContext db,
            IPasswordHasher passwordHasher,
            CancellationToken cancellationToken)
        {
            var presentedHash = PasswordResetTokenGenerator.Hash(request.Token);

            var resetToken = await db.PasswordResetTokens
                .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

            // BR-11: single use. A link that still works after it has been redeemed is a standing
            // back door into the account for anyone who later reads the mailbox.
            if (resetToken is null || resetToken.UsedAtUtc is not null || resetToken.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return InvalidToken();
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == resetToken.UserId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                return InvalidToken();
            }

            var now = DateTime.UtcNow;

            user.PasswordHash = passwordHasher.Hash(request.NewPassword);
            user.UpdatedAtUtc = now;
            resetToken.UsedAtUtc = now;

            // BR-11: the usual reason to reset is believing somebody else has the password. Leaving
            // their sessions alive would defeat the reset — their refresh token outlives it by up to
            // RefreshTokenLifetimeDays, and rotation would keep renewing it indefinitely.
            var activeRefreshTokens = await db.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var token in activeRefreshTokens)
            {
                token.RevokedAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/reset-password", Handler.Handle)
            .WithValidation<Request>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AuthPolicyName)
            .WithTags("Auth");
}
