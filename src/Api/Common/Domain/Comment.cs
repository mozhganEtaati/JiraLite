namespace JiraLite.Api.Common.Domain;

/// <summary>Discussion entry on an Issue. spec/18-database.md §7, spec/10-comments.md.</summary>
public class Comment
{
    public Guid Id { get; init; }
    public Guid IssueId { get; init; }
    public Guid AuthorUserId { get; init; }
    public required string Body { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; set; }
}
