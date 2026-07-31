using JiraLite.Api.Common.Ranking;
using Xunit;

namespace JiraLite.Api.IntegrationTests.Ranking;

public class LexoRankTests
{
    [Fact]
    public void Initial_matches_the_spec_illustrative_first_rank()
    {
        Assert.Equal("0|100000:", LexoRank.Initial());
    }

    [Fact]
    public void Between_null_and_null_equals_Initial()
    {
        Assert.Equal(LexoRank.Initial(), LexoRank.Between(null, null));
    }

    [Fact]
    public void Next_is_strictly_greater_than_the_last_rank()
    {
        var first = LexoRank.Initial();
        var second = LexoRank.Next(first);
        var third = LexoRank.Next(second);

        Assert.True(string.CompareOrdinal(second, first) > 0);
        Assert.True(string.CompareOrdinal(third, second) > 0);
    }

    [Fact]
    public void Between_two_ranks_with_integer_room_is_strictly_between()
    {
        var lower = LexoRank.Initial();
        var upper = LexoRank.Next(lower);

        var mid = LexoRank.Between(lower, upper);

        Assert.True(string.CompareOrdinal(mid, lower) > 0);
        Assert.True(string.CompareOrdinal(mid, upper) < 0);
    }

    [Fact]
    public void Repeated_inserts_between_the_same_two_neighbors_keep_converging()
    {
        var lower = LexoRank.Initial();
        var upper = LexoRank.Next(lower);

        var current = lower;
        for (var i = 0; i < 10; i++)
        {
            var next = LexoRank.Between(current, upper, maxRankLength: 60);
            Assert.True(string.CompareOrdinal(next, current) > 0);
            Assert.True(string.CompareOrdinal(next, upper) < 0);
            current = next;
        }
    }

    [Fact]
    public void Between_null_lower_and_a_rank_prepends_correctly()
    {
        var upper = LexoRank.Initial();

        var prepended = LexoRank.Between(null, upper);

        Assert.True(string.CompareOrdinal(prepended, upper) < 0);
    }

    [Fact]
    public void Exceeding_max_rank_length_throws_precision_exhausted()
    {
        var lower = LexoRank.Initial();
        var upper = LexoRank.Next(lower);
        var current = lower;

        Assert.Throws<RankPrecisionExhaustedException>(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                current = LexoRank.Between(current, upper, maxRankLength: 15);
            }
        });
    }

    [Fact]
    public void Adjacent_integers_with_no_room_fall_back_to_fraction()
    {
        // Two ranks whose integer parts differ by exactly 1 (no integer gap).
        var lower = "0|100000:";
        var upper = "0|100001:";

        var mid = LexoRank.Between(lower, upper);

        Assert.True(string.CompareOrdinal(mid, lower) > 0);
        Assert.True(string.CompareOrdinal(mid, upper) < 0);
    }

    [Fact]
    public void Appending_past_max_integer_falls_back_to_fraction_instead_of_failing()
    {
        var nearMax = "0|999950:";

        var appended = LexoRank.Next(nearMax);
        var appendedAgain = LexoRank.Next(appended);

        Assert.True(string.CompareOrdinal(appended, nearMax) > 0);
        Assert.True(string.CompareOrdinal(appendedAgain, appended) > 0);
        Assert.StartsWith("0|999950:", appended);
    }
}
