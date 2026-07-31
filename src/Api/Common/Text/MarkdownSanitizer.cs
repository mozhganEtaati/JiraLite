using System.Text.RegularExpressions;

namespace JiraLite.Api.Common.Text;

/// <summary>
/// Baseline stripping of embedded script-like content before storing user-supplied Markdown.
/// Rendering itself happens client-side (spec/00-project-overview.md Assumption 11); this is not a
/// full HTML sanitizer, only the minimum safety baseline spec/09-issues.md NFR-01 and
/// spec/10-comments.md NFR-01 both call for.
/// </summary>
public static partial class MarkdownSanitizer
{
    public static string? Strip(string? input) => input is null ? null : ScriptTagPattern().Replace(input, string.Empty);

    [GeneratedRegex(@"<script\b[^>]*>.*?</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptTagPattern();
}
