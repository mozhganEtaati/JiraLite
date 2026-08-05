using FluentValidation;
using Hangfire;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.BackgroundJobs;
using JiraLite.Api.Common.Infrastructure.Persistence;
using JiraLite.Api.Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Features.Auth;

/// <summary>
/// spec/01-authentication.md FR-06, BR-09, BR-10, BR-12, NFR-06.
///
/// Answers 202 on every path — unknown address, deactivated account, live account alike. Login
/// already refuses to say whether an email is registered (NFR-03); an endpoint here that answered
/// honestly would hand back exactly the oracle login withholds, and it would do so without needing
/// a password guess.
///
/// The response body is identical, but the two paths are not equally fast: a hit writes rows and
/// enqueues a job. That timing difference is a known residual channel, left as-is because closing
/// it means padding every request to a fixed floor — the per-IP limiter (NFR-04) is what makes it
/// impractical to sample at the volume enumeration would need.
/// </summary>
public static class ForgotPassword
{
    public record Request(string Email);

    public record Response(string Message);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Request request,
            JiraLiteDbContext db,
            IOptions<PasswordResetOptions> passwordResetOptions,
            IBackgroundJobClient backgroundJobClient,
            CancellationToken cancellationToken)
        {
            var normalizedEmail = request.Email.Trim();

            var user = await db.Users
                .SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);

            // BR-12: a deactivated user cannot authenticate, so a reset must not be a way back in.
            if (user is null || !user.IsActive)
            {
                return Accepted();
            }

            var now = DateTime.UtcNow;

            // BR-10: only the newest link works. Anything else means every link ever requested
            // stays live for its full window, which multiplies the exposure of a mailbox breach.
            var outstanding = await db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.UsedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var token in outstanding)
            {
                token.UsedAtUtc = now;
            }

            var (rawToken, tokenHash) = PasswordResetTokenGenerator.Create();
            var options = passwordResetOptions.Value;

            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAtUtc = now.AddMinutes(options.TokenLifetimeMinutes),
                CreatedAtUtc = now
            });

            await db.SaveChangesAsync(cancellationToken);

            // spec/13-notifications.md NFR-01 — off the request thread, and no Notification row:
            // someone who cannot log in cannot read an in-app notification.
            backgroundJobClient.Enqueue<SendEmailJob>(job => job.Execute(
                user.Email,
                "Reset your JiraLite password",
                $"We received a request to reset the password for your JiraLite account. " +
                $"{options.BuildResetInstruction(rawToken)} " +
                $"The link expires in {options.TokenLifetimeMinutes} minutes and can be used once. " +
                $"If you did not ask for this, ignore this email — your password has not changed.",
                CancellationToken.None));

            return Accepted();
        }

        /// <summary>
        /// One response object for both outcomes, built in one place so the two paths cannot drift
        /// apart into a distinguishable pair.
        /// </summary>
        private static IResult Accepted() =>
            Results.Accepted(value: new Response(
                "If that email is registered, a password reset link is on its way."));
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/forgot-password", Handler.Handle)
            .WithValidation<Request>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AuthPolicyName)
            .WithTags("Auth");
}
