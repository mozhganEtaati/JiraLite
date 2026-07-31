using System.Security.Claims;
using FluentValidation;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-01.</summary>
public static class CreateOrganization
{
    public record Request(string Name);

    public record Response(Guid Id, string Name, Guid OwnerUserId, DateTime CreatedAtUtc);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
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
            var now = DateTime.UtcNow;
            var organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                OwnerUserId = caller.GetUserId(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/organizations/{organization.Id}",
                new Response(organization.Id, organization.Name, organization.OwnerUserId, organization.CreatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/organizations", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization()
            .WithTags("Workspaces");
}
