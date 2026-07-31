namespace JiraLite.Api.Common.Infrastructure.FileStorage;

/// <summary>spec/11-attachments.md NFR-01 — max Attachment size, configurable via application settings.</summary>
public class AttachmentOptions
{
    public const string SectionName = "Attachments";

    public long MaxSizeBytes { get; set; } = 25 * 1024 * 1024;
}
