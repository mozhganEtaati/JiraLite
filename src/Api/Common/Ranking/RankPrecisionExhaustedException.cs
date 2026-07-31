namespace JiraLite.Api.Common.Ranking;

/// <summary>
/// Thrown when repeated insertions between the same two neighboring ranks have exhausted the
/// configured precision budget (spec/07-backlog.md BR-03). Callers should still complete the
/// current request with a fallback rank and enqueue RebalanceRanksJob — this is a signal to
/// rebalance, not a request failure.
/// </summary>
public class RankPrecisionExhaustedException(string message) : Exception(message);
