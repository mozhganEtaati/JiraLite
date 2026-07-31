namespace JiraLite.Api.Common.Ranking;

/// <summary>spec/07-backlog.md BR-03 — how much rank-string growth is tolerated before a rebalance is required.</summary>
public class RankingOptions
{
    public const string SectionName = "Ranking";

    /// <summary>Comfortably under the Issue.Rank string(255) column limit (spec/18-database.md §7).</summary>
    public const int DefaultMaxRankLength = 40;

    public int MaxRankLength { get; set; } = DefaultMaxRankLength;
}
