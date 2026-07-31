using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Teams;

/// <summary>spec/04-teams.md FR-04.</summary>
public static class ListTeams
{
    public record TeamItem(Guid Id, string Name, string? Description, DateTime CreatedAtUtc);

    public record Response(IReadOnlyList<TeamItem> Items);

    public static class Handler
    {
        public static async Task<IResult> Handle(
            Guid workspaceId,
            JiraLiteDbContext db,
            CancellationToken cancellationToken)
        {
            var items = await db.Teams
                .Where(t => t.WorkspaceId == workspaceId)
                .OrderBy(t => t.Name)
                .Select(t => new TeamItem(t.Id, t.Name, t.Description, t.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new Response(items));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/teams", Handler.Handle)
            .RequireAuthorization("WorkspaceMember")
            .WithTags("Teams");
}
