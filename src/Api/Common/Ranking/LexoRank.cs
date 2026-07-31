using System.Text;

namespace JiraLite.Api.Common.Ranking;

/// <summary>
/// Fractional-indexing rank strings for the Product/Sprint Backlog, per spec/07-backlog.md
/// BR-01/BR-02: a lexicographically sortable string column, comparable with plain ordinal string
/// comparison, that supports inserting a new value strictly between any two neighbors without
/// rewriting the rest of the list (NFR-01).
///
/// Format: "{bucket}|{integer:D6}:{fraction}" — e.g. "0|100000:". The integer part is the common
/// case (cheap, fixed-width, appends/inserts with room just pick a new integer); the fraction part
/// (a base-36 string, longer = smaller increments) only grows when two neighbors are adjacent
/// integers or share the same one, i.e. when the integer part has no room left. `bucket` is
/// currently always "0" — bucket rotation is a documented future improvement, not needed for V1's
/// single-bucket usage.
/// </summary>
public static class LexoRank
{
    private const string DefaultBucket = "0";
    private const int IntegerWidth = 6;
    private const int MaxInteger = 999_999;
    private const int InitialInteger = 100_000;
    private const int AppendStep = 100;
    private const string DigitAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int Radix = 36;

    /// <summary>"0|" + 6-digit integer + ":" — every rank pays this much overhead before the fraction starts.</summary>
    private const int FixedOverheadLength = 2 + IntegerWidth + 1;

    /// <summary>First rank for an empty list.</summary>
    public static string Initial() => Between(null, null);

    /// <summary>Rank for appending to the bottom of a list whose current last item has <paramref name="lastRank"/>.</summary>
    public static string Next(string lastRank, int maxRankLength = RankingOptions.DefaultMaxRankLength) =>
        Between(lastRank, null, maxRankLength);

    /// <summary>
    /// A rank strictly between <paramref name="lowerRank"/> (exclusive, null = no lower bound) and
    /// <paramref name="upperRank"/> (exclusive, null = no upper bound / append). Throws
    /// <see cref="RankPrecisionExhaustedException"/> if <paramref name="maxRankLength"/> would be
    /// exceeded — the caller should still be able to recover by rebalancing the list.
    /// </summary>
    public static string Between(string? lowerRank, string? upperRank, int maxRankLength = RankingOptions.DefaultMaxRankLength)
    {
        var (lowerInteger, lowerFraction) = lowerRank is null ? (0, "") : Parse(lowerRank);

        int resultInteger;
        string resultFraction;

        if (upperRank is null)
        {
            if (lowerRank is null)
            {
                resultInteger = InitialInteger;
                resultFraction = "";
            }
            else if (lowerInteger + AppendStep <= MaxInteger)
            {
                resultInteger = lowerInteger + AppendStep;
                resultFraction = "";
            }
            else
            {
                resultInteger = lowerInteger;
                resultFraction = FractionBetween(lowerFraction, null, maxRankLength);
            }
        }
        else
        {
            var (upperInteger, upperFraction) = Parse(upperRank);

            if (upperInteger - lowerInteger >= 2)
            {
                resultInteger = lowerInteger + (upperInteger - lowerInteger) / 2;
                resultFraction = "";
            }
            else if (upperInteger == lowerInteger)
            {
                resultInteger = lowerInteger;
                resultFraction = FractionBetween(lowerFraction, upperFraction, maxRankLength);
            }
            else
            {
                // upperInteger == lowerInteger + 1: no integer room; any (lowerInteger, *) sorts
                // below (lowerInteger + 1, *) regardless of fraction, so the fraction is unbounded above.
                resultInteger = lowerInteger;
                resultFraction = FractionBetween(lowerFraction, null, maxRankLength);
            }
        }

        return Format(resultInteger, resultFraction);
    }

    private static string FractionBetween(string lowerFraction, string? upperFraction, int maxRankLength)
    {
        var maxFractionLength = Math.Max(1, maxRankLength - FixedOverheadLength);
        var prefix = new StringBuilder();
        var i = 0;

        while (true)
        {
            var lowerDigit = i < lowerFraction.Length ? DigitValue(lowerFraction[i]) : 0;
            var upperDigit = upperFraction is null ? Radix : (i < upperFraction.Length ? DigitValue(upperFraction[i]) : 0);

            if (upperDigit - lowerDigit >= 2)
            {
                prefix.Append(DigitChar(lowerDigit + (upperDigit - lowerDigit) / 2));
                return prefix.ToString();
            }

            prefix.Append(DigitChar(lowerDigit));
            i++;

            if (prefix.Length >= maxFractionLength)
            {
                throw new RankPrecisionExhaustedException(
                    "No more precision is available between these two neighboring ranks; the list needs rebalancing.");
            }
        }
    }

    private static int DigitValue(char c) => DigitAlphabet.IndexOf(c);

    private static char DigitChar(int value) => DigitAlphabet[value];

    private static (int Integer, string Fraction) Parse(string rank)
    {
        var afterBucket = rank[(rank.IndexOf('|') + 1)..];
        var colonIndex = afterBucket.IndexOf(':');
        var integerPart = afterBucket[..colonIndex];
        var fractionPart = afterBucket[(colonIndex + 1)..];
        return (int.Parse(integerPart), fractionPart);
    }

    private static string Format(int integer, string fraction) =>
        $"{DefaultBucket}|{integer.ToString().PadLeft(IntegerWidth, '0')}:{fraction}";
}
