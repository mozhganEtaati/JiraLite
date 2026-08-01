using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Auth;

/// <summary>spec/01-authentication.md FR-04, BR-07. Revokes only the presented refresh token.</summary>
public static class Logout
{
    public record Request(string RefreshToken);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty();
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Request request,
            System.Security.Claims.ClaimsPrincipal caller,
            JiraLiteDbContext db,
            ITokenService tokenService,
            CancellationToken cancellationToken)
        {
            var callerUserId = caller.GetUserId();
            var presentedHash = tokenService.HashRefreshToken(request.RefreshToken);

            var token = await db.RefreshTokens
                .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

            if (token is null)
            {
                return ProblemResults.Unauthorized(
                    "https://jiralite.dev/errors/invalid-refresh-token",
                    "Refresh token is invalid.");
            }

            if (token.UserId != callerUserId)
            {
                return ProblemResults.Forbidden(
                    "https://jiralite.dev/errors/refresh-token-not-owned",
                    "This refresh token does not belong to the authenticated user.");
            }

            token.RevokedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/logout", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingSetup.AuthPolicyName)
            .WithTags("Auth");
}
