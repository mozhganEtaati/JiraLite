using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Auth;

/// <summary>spec/01-authentication.md FR-01, BR-01, BR-02. Creates a User only — no auto-login.</summary>
public static class Register
{
    public record Request(string Email, string Password);

    public record Response(Guid Id, string Email, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
                .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
                .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
        }
    }

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Request request,
            JiraLiteDbContext db,
            Common.Auth.IPasswordHasher passwordHasher,
            CancellationToken cancellationToken)
        {
            var normalizedEmail = request.Email.Trim();

            var emailExists = await db.Users
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);
            if (emailExists)
            {
                return ProblemResults.Conflict(
                    "https://jiralite.dev/errors/email-already-registered",
                    "This email is already registered.");
            }

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                PasswordHash = passwordHasher.Hash(request.Password),
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Users.Add(user);

            // spec/02-users.md FR-01, BR-01: UserProfile + NotificationPreference auto-created with defaults.
            var displayName = normalizedEmail.Contains('@') ? normalizedEmail[..normalizedEmail.IndexOf('@')] : normalizedEmail;
            db.UserProfiles.Add(new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DisplayName = displayName,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            db.NotificationPreferences.Add(new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                EmailEnabled = true,
                InAppEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/users/{user.Id}", new Response(user.Id, user.Email, user.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/register", Handler.Handle)
            .WithValidation<Request>()
            .AllowAnonymous()
            .WithTags("Auth");
}
