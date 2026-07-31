namespace JiraLite.Api.Common.Infrastructure.FileStorage;

/// <summary>
/// Storage abstraction so a future move to blob storage is a configuration change,
/// not a rewrite. spec/11-attachments.md, spec/00-project-overview.md §5 principle 3.
/// V1 implementation is local disk (LocalDiskFileStorage). First consumer is Avatar
/// upload (spec/02-users.md); reused unchanged by Attachments in Phase 4.
/// </summary>
public interface IFileStorage
{
    /// <summary>Saves content under the given category (e.g. "avatars") and returns an opaque storage key.</summary>
    Task<string> SaveAsync(string category, string suggestedFileName, Stream content, CancellationToken cancellationToken);

    /// <summary>Deletes the file for a storage key. No-op if it doesn't exist.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Opens the file for a storage key, or null if it doesn't exist.</summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Builds a client-facing URL for a storage key (spec/02-users.md AvatarUrl).</summary>
    string GetPublicUrl(string storageKey);
}
