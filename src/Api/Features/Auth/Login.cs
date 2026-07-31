using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Auth;

/// <summary>spec/01-authentication.md FR-02, NFR-03, BR-08. Generic failure message — never reveals whether the email exists or is deactivated.</summary>
public static class Login
{
    public record Request(string Email, string Password);

    public record Response(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty();
        }
    }

    public static class Handler
    {
        private const string InvalidCredentialsMessage = "Invalid email or password.";

        public static async Task<IResult> Handle(
            Request request,
            JiraLiteDbContext db,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            CancellationToken cancellationToken)
        {
            var user = await db.Users
                .SingleOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower(), cancellationToken);

            if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
            {
                return ProblemResults.Unauthorized(
                    "https://jiralite.dev/errors/invalid-credentials",
                    InvalidCredentialsMessage);
            }

            var (accessToken, accessExpiresAtUtc) = tokenService.CreateAccessToken(user);
            var (rawRefreshToken, tokenHash, refreshExpiresAtUtc) = tokenService.CreateRefreshToken();

            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAtUtc = refreshExpiresAtUtc,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(accessToken, accessExpiresAtUtc, rawRefreshToken, refreshExpiresAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/login", Handler.Handle)
            .WithValidation<Request>()
            .AllowAnonymous()
            .WithTags("Auth");
}
