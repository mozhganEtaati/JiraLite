using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Users;

/// <summary>
/// spec/23-mcp-server.md FR-02, FR-03, BR-03 (≤365 days), BR-05 (≤10 active).
/// The plaintext value is returned here and nowhere else — only its hash is persisted (NFR-01).
/// </summary>
public static class CreateAccessToken
{
    /// <summary>BR-05 — creating an 11th is rejected rather than silently evicting an existing token.</summary>
    public const int MaxActiveTokensPerUser = 10;

    public const int MaxLifetimeDays = 365;

    public record Request(string Name, int ExpiresInDays);

    public record Response(Guid Id, string Name, string Token, DateTime ExpiresAtUtc, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, MaxLifetimeDays)
                .WithMessage($"ExpiresInDays must be between 1 and {MaxLifetimeDays}.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Request request,
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var now = DateTime.UtcNow;

            var activeCount = await db.PersonalAccessTokens
                .CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now, cancellationToken);
            if (activeCount >= MaxActiveTokensPerUser)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/too-many-access-tokens",
                    $"You already hold {MaxActiveTokensPerUser} active tokens. Revoke one before creating another.");
            }

            var (rawToken, tokenHash) = PersonalAccessTokenGenerator.Create();

            var token = new PersonalAccessToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = request.Name.Trim(),
                TokenHash = tokenHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(request.ExpiresInDays)
            };
            db.PersonalAccessTokens.Add(token);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/users/me/tokens/{token.Id}",
                new Response(token.Id, token.Name, rawToken, token.ExpiresAtUtc, token.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/users/me/tokens", Handler.Handle)
            .RequireAuthorization()
            .WithValidation<Request>()
            .WithTags("Users");
}
