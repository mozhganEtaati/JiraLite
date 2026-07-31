using FluentValidation;
using JiraLite.Api.Common.Behaviors;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9 — Owner only.</summary>
public static class RenameOrganization
{
    public record Request(string Name);

    public record Response(Guid Id, string Name, DateTime UpdatedAtUtc);

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
            Guid orgId,
            Request request,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == orgId, cancellationToken);
            if (organization is null)
            {
                return Results.NotFound();
            }

            organization.Name = request.Name.Trim();
            organization.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new Response(organization.Id, organization.Name, organization.UpdatedAtUtc));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/organizations/{orgId:guid}", Handler.Handle)
            .WithValidation<Request>()
            .RequireAuthorization("OrganizationOwner")
            .WithTags("Workspaces");
}
