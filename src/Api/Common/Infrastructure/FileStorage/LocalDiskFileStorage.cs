using Microsoft.Extensions.Options;

namespace JiraLite.Api.Common.Infrastructure.FileStorage;

/// <summary>
/// V1 file storage: local disk under a Docker-mounted volume. spec/11-attachments.md,
/// spec/02-users.md BR-02. Files are served back via static-file middleware mounted at
/// "/files" in Program.cs.
/// </summary>
public class LocalDiskFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<string> SaveAsync(string category, string suggestedFileName, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(suggestedFileName);
        // NFR-03: generated filename on disk, never the client-supplied original — avoids path traversal/collisions.
        var storageKey = $"{category}/{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(_options.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var fileStream = File.Create(physicalPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storageKey;
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var physicalPath = Path.Combine(_options.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var physicalPath = Path.Combine(_options.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        Stream? stream = File.Exists(physicalPath) ? File.OpenRead(physicalPath) : null;
        return Task.FromResult(stream);
    }

    /// <summary>
    /// Root-relative unless a deployment names a base URL of its own.
    /// <para>
    /// This value is persisted (UserProfile.AvatarUrl), so it must not carry anything
    /// about the request that happened to write it. Building it from Scheme/Host baked
    /// the caller's Host header into the database: behind a proxy that is the internal
    /// address rather than the public one, moving hosts stranded every stored URL, and
    /// a forged Host header ended up stored and served to other people.
    /// </para>
    /// <para>
    /// A relative URL resolves against whatever origin served the page, which is the
    /// same origin that proxies "/files" — so it stays correct wherever it is read.
    /// Set PublicBaseUrl when the files are served from somewhere else entirely.
    /// </para>
    /// </summary>
    public string GetPublicUrl(string storageKey)
    {
        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/files/{storageKey}";
        }

        return $"/files/{storageKey}";
    }
}
