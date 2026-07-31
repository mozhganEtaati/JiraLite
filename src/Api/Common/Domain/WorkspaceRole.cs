namespace JiraLite.Api.Common.Domain;

/// <summary>spec/16-rbac.md — WorkspaceMember.Role values. "Member" is the required non-admin baseline, not a named platform role.</summary>
public static class WorkspaceRole
{
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly string[] All = [Admin, Member];
}
