using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Projects;

/// <summary>spec/05-projects.md §9, §14.</summary>
public static class ListProjectMembers
{
    public record MemberItem(Guid UserId, string Role, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<MemberItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid projectId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            var items = await db.ProjectMembers
                .Where(m => m.ProjectId == projectId)
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => new MemberItem(m.UserId, m.Role, m.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/projects/{projectId:guid}/members", Handler.Handle)
            .RequireAuthorization("ProjectView")
            .WithTags("Projects");
}
