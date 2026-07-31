namespace JiraLite.Api.Common.Domain;

/// <summary>spec/09-issues.md BR-14 — Issue.Priority values; defaults to Medium.</summary>
public static class IssuePriority
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";

    public static readonly string[] All = [Low, Medium, High, Critical];
}
