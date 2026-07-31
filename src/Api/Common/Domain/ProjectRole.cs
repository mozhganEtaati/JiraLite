namespace JiraLite.Api.Common.Domain;

/// <summary>spec/16-rbac.md — ProjectMember.Role values.</summary>
public static class ProjectRole
{
    public const string ProjectAdmin = "ProjectAdmin";
    public const string Developer = "Developer";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [ProjectAdmin, Developer, Viewer];
}
