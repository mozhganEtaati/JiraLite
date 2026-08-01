using System.Security.Claims;
using JiraLite.Api.Common.Auth;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Dashboard;

/// <summary>spec/14-dashboard.md FR-02, BR-03 — Projects the caller has an explicit ProjectMember record on.</summary>
public static class GetMyProjects
{
    public record ProjectItem(Guid Id, string Key, string Name, string Role);

    public record Response(IReadOnlyList<ProjectItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(ClaimsPrincipal caller, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var userId = caller.GetUserId();

            var items = await db.ProjectMembers
                .Where(m => m.UserId == userId)
                .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => new { m, p })
                .OrderBy(x => x.p.Name)
                .Select(x => new ProjectItem(x.p.Id, x.p.Key, x.p.Name, x.m.Role))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/my-projects", Handler.Handle)
            .RequireAuthorization()
            .WithTags("Dashboard");
}
