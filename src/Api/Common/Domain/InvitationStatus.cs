namespace JiraLite.Api.Common.Domain;

/// <summary>spec/03-workspaces.md — Invitation.Status values.</summary>
public static class InvitationStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
}
