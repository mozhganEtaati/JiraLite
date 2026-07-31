namespace JiraLite.Api.Common.Domain;

/// <summary>
/// Minimal, reused-everywhere reference to a User (assignee, reporter, author, ...).
/// spec/19-api-guidelines.md §7, spec/20-coding-guidelines.md §4.
/// </summary>
public record UserSummary(Guid Id, string DisplayName, string? AvatarUrl);
