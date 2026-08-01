using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Auth;

/// <summary>spec/01-authentication.md FR-03, BR-03, BR-04, BR-08. Single-use rotation with reuse-detection.</summary>
public static class Refresh
{
    public record Request(string RefreshToken);

    public record Response(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty();
        }
    }

    public static class Handler
    {
        private static IResult InvalidToken() =>
            ProblemResults.Unauthorized("https://jiralite.dev/errors/invalid-refresh-token", "Refresh token is invalid or expired.");

        public static async Task<IResult> Handle(
            Request request,
            JiraLiteDbContext db,
            ITokenService tokenService,
            CancellationToken cancellationToken)
        {
            var presentedHash = tokenService.HashRefreshToken(request.RefreshToken);

            var presentedToken = await db.RefreshTokens
                .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

            if (presentedToken is null)
            {
                return InvalidToken();
            }

            // BR-04: reuse of an already-revoked token is treated as possible theft — revoke the whole family.
            if (presentedToken.RevokedAtUtc is not null)
            {
                var activeFamilyTokens = await db.RefreshTokens
                    .Where(t => t.UserId == presentedToken.UserId && t.RevokedAtUtc == null)
                    .ToListAsync(cancellationToken);

                var now = DateTime.UtcNow;
                foreach (var token in activeFamilyTokens)
                {
                    token.RevokedAtUtc = now;
                }
                if (activeFamilyTokens.Count > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }

                return InvalidToken();
            }

            if (presentedToken.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return InvalidToken();
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == presentedToken.UserId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                return InvalidToken();
            }

            var (accessToken, accessExpiresAtUtc) = tokenService.CreateAccessToken(user);
            var (rawRefreshToken, newTokenHash, refreshExpiresAtUtc) = tokenService.CreateRefreshToken();

            var newToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = newTokenHash,
                ExpiresAtUtc = refreshExpiresAtUtc,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.RefreshTokens.Add(newToken);

            presentedToken.RevokedAtUtc = DateTime.UtcNow;
            presentedToken.ReplacedByTokenId = newToken.Id;

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(accessToken, accessExpiresAtUtc, rawRefreshToken, refreshExpiresAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/refresh", Handler.Handle)
            .WithValidation<Request>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AuthPolicyName)
            .WithTags("Auth");
}
