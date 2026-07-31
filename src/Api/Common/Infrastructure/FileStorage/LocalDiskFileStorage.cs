using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace JiraLite.Api.Common.Infrastructure.FileStorage;

/// <summary>
/// V1 file storage: local disk under a Docker-mounted volume. spec/11-attachments.md,
/// spec/02-users.md BR-02. Files are served back via static-file middleware mounted at
/// "/files" in Program.cs.
/// </summary>
public class LocalDiskFileStorage(
    IOptions<FileStorageOptions> options,
    IHttpContextAccessor httpContextAccessor) : IFileStorage
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

    public string GetPublicUrl(string storageKey)
    {
        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/files/{storageKey}";
        }

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return $"/files/{storageKey}";
        }

        return $"{request.Scheme}://{request.Host}/files/{storageKey}";
    }
}
