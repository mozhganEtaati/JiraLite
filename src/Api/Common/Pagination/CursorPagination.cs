using System.Text;
using System.Text.Json;

namespace JiraLite.Api.Common.Pagination;

/// <summary>
/// Opaque, server-generated cursor tokens for list endpoints, per spec/19-api-guidelines.md §5.
/// V1 encodes a plain offset — clients must treat the string as opaque and never construct one.
/// </summary>
public static class CursorPagination
{
    public record PageInfo(bool HasNextPage, string? NextCursor);

    private record CursorPayload(int Offset);

    public static string EncodeOffset(int offset)
    {
        var json = JsonSerializer.Serialize(new CursorPayload(offset));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static int DecodeOffset(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var payload = JsonSerializer.Deserialize<CursorPayload>(json)
            ?? throw new FormatException("Cursor payload deserialized to null.");
        return payload.Offset;
    }
}
