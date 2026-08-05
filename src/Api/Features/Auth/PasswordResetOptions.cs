namespace JiraLite.Api.Features.Auth;

/// <summary>
/// spec/20-coding-guidelines.md §8 — reset link lifetime and shape are configuration, not constants.
/// spec/01-authentication.md BR-09.
/// </summary>
public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// Deliberately far shorter than an invitation's 7 days: this link is a live credential to an
    /// account that already exists, so its window is measured in minutes.
    /// </summary>
    public int TokenLifetimeMinutes { get; init; } = 60;

    /// <summary>
    /// Where the emailed link points, with <c>{token}</c> substituted. Left blank the mail carries
    /// the bare token instead, matching the invitation email — the API has no opinion about which
    /// front end (if any) is deployed in front of it.
    /// </summary>
    public string ResetUrlTemplate { get; init; } = string.Empty;

    public const string TokenPlaceholder = "{token}";

    public string BuildResetInstruction(string token) =>
        string.IsNullOrWhiteSpace(ResetUrlTemplate)
            ? $"Use this password reset token: {token}"
            : $"Reset your password here: {ResetUrlTemplate.Replace(TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal)}";
}
