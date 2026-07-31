using JiraLite.Api.Common.Pagination;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Pagination;

public class CursorPaginationTests
{
    [Fact]
    public void Encode_then_decode_round_trips_the_offset()
    {
        var cursor = CursorPagination.EncodeOffset(25);

        var decoded = CursorPagination.DecodeOffset(cursor);

        Assert.Equal(25, decoded);
    }

    [Fact]
    public void Decode_of_null_cursor_returns_zero()
    {
        Assert.Equal(0, CursorPagination.DecodeOffset(null));
    }

    [Fact]
    public void Decode_of_malformed_cursor_throws_a_bad_request_friendly_exception()
    {
        Assert.Throws<FormatException>(() => CursorPagination.DecodeOffset("not-a-valid-cursor"));
    }
}
