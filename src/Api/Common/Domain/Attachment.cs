namespace JiraLite.Api.Common.Domain;

/// <summary>Uploaded file on an Issue, stored via IFileStorage. spec/18-database.md §7, spec/11-attachments.md.</summary>
public class Attachment
{
    public Guid Id { get; init; }
    public Guid IssueId { get; init; }
    public Guid UploadedByUserId { get; init; }
    public required string FileName { get; init; }
    public required string StorageKey { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
