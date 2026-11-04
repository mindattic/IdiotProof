using IdiotProof.Evidence.Leaders;
using IdiotProof.Models;
using IdiotProof.Shared;

namespace IdiotProof.Strategies.Backtesting;

/// <summary>
/// The leaders a backtested day reads for LeaderConfirms / NotLeaderDiverging: links discovered from
/// sessions before that day, plus each leader's bars for the day. A reading as of a bar only uses
/// leader bars that had closed by then, so a later leader bar can never decide an earlier entry.
/// </summary>
public sealed record BacktestLeaderContext(
    IReadOnlyList<LeaderLink> Links,
    IReadOnlyDictionary<string, IReadOnlyList<Candle>> Bars,
    int LookbackBars = 5)
{
    public List<LeaderReading> ReadingsAsOf(DateTime asOfUtc) =>
        Links.Select(l => new LeaderReading(l.Symbol, l.Hops, l.Influence,
                Bars.TryGetValue(l.Symbol, out var bars) ? LeaderSignal.RecentMove(bars, asOfUtc, LookbackBars) : null))
            .ToList();
}
