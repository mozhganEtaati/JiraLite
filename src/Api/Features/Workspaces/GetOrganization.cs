using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md §9 — Owner only (OrganizationOwnerRequirement).</summary>
public static class GetOrganization
{
    public record Response(Guid Id, string Name, Guid OwnerUserId, DateTime CreatedAtUtc);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid orgId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var response = await db.Organizations
                .Where(o => o.Id == orgId)
                .Select(o => new Response(o.Id, o.Name, o.OwnerUserId, o.CreatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            return response is null ? Results.NotFound() : Results.Ok(response);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/organizations/{orgId:guid}", Handler.Handle)
            .RequireAuthorization("OrganizationOwner")
            .WithTags("Workspaces");
}
