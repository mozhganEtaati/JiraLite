using JiraLite.Api.Common.Domain;
using JiraLite.Api.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JiraLite.Api.Features.Admin;

/// <summary>spec/17-admin.md FR-04, BR-03 — static role catalog, identical for every Workspace, sourced from spec/16-rbac.md §14.</summary>
public static class GetRoleCatalog
{
    public record RoleItem(string Scope, string Role, string Description);

    public record Response(IReadOnlyList<RoleItem> Items);

    private static readonly IReadOnlyList<RoleItem> Catalog =
    [
        new("Workspace", WorkspaceRole.Admin, "Full authority over the Workspace and every Project within it."),
        new("Workspace", WorkspaceRole.Member, "Baseline Workspace membership; no elevated rights."),
        new("Project", ProjectRole.ProjectAdmin, "Full authority over a single Project, its Boards, and members."),
        new("Project", ProjectRole.Developer, "Can create and edit Issues, Comments, Attachments, and Sprints."),
        new("Project", ProjectRole.Viewer, "Read-only access to a Project.")
    ];

    public static class Handler
    {
        public static async Task<IResult> Handle(Guid workspaceId, JiraLiteDbContext db, CancellationToken cancellationToken)
        {
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
            {
                return Results.NotFound();
            }

            return Results.Ok(new Response(Catalog));
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/workspaces/{workspaceId:guid}/admin/roles", Handler.Handle)
            .RequireAuthorization("WorkspaceAdmin")
            .WithTags("Admin");
}
