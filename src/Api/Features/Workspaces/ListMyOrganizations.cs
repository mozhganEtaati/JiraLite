using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md FR-09.</summary>
public static class ListMyOrganizations
{
    public record OrganizationItem(Guid Id, string Name, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<OrganizationItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            ClaimsPrincipal caller,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();
            var items = await db.Organizations
                .Where(o => o.OwnerUserId == userId)
                .OrderBy(o => o.CreatedAtUtc)
                .Select(o => new OrganizationItem(o.Id, o.Name, o.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/organizations", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Workspaces");
}
