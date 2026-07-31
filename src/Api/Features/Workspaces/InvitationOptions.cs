namespace JiraLite.Api.Features.Workspaces;

/// <summary>spec/03-workspaces.md BR-07 — configurable invitation expiry.</summary>
public class InvitationOptions
{
    public const string SectionName = "Invitations";

    public int ExpiryDays { get; init; } = 7;
}
