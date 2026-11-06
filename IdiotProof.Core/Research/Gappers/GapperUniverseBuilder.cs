using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Models;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One raw daily bar as the universe rule consumes it.</summary>
public sealed record StudyDailyBar(DateOnly DateEt, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>What the universe build counted besides the rows: symbols whose bar history brackets a long gap (reused tickers).</summary>
public sealed record UniverseBuildStats(int Symbols, int ReusedTickerSymbols, int UniverseDays, int Candidates, Dictionary<string, int> ExcludedByReason);

/// <summary>
/// The frozen universe rule (protocol §Universe) applied day by day: U(D) from daily bars dated
/// strictly before D (split-adjusted prior close in the price band, ADV20$ floor, session floor),
/// the split-window and unexplained-move exclusions, the candidate test from D's own bar, ADV
/// deciles within U(D) and the price tier. Pure: bars and splits in, rows out.
/// </summary>
public static class GapperUniverseBuilder
{
    public const string ExcludedSplitWindow = "split-window";
    public const string ExcludedUnexplainedMove = "unexplained-move";

    public static (List<GapperStudyUniverseDay> Rows, UniverseBuildStats Stats) Build(
        GapperStudyParams p,
        IReadOnlyDictionary<string, IReadOnlyList<StudyDailyBar>> dailyBySymbol,
        IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>> splitsBySymbol,
        IReadOnlyDictionary<string, string> assetNames,
        DateOnly from, DateOnly to,
        Func<DateOnly, string> splitOf)
    {
        var (rows, stats) = BuildRows(p, dailyBySymbol, splitsBySymbol, assetNames, from, to, splitOf);
        foreach (var day in rows.GroupBy(r => r.DateEt)) AssignDeciles(day);
        return (rows.OrderBy(r => r.DateEt).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList(), stats);
    }

    /// <summary>ADV20$ deciles within one day's U(D): rank among non-excluded members, lowest dollar volume first, decile = rank x 10 / n.</summary>
    public static void AssignDeciles(IEnumerable<GapperStudyUniverseDay> dayRows)
    {
        var members = dayRows.Where(r => r.ExcludedReason is null).OrderBy(r => r.Adv20Dollars).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToList();
        for (int i = 0; i < members.Count; i++) members[i].AdvDecile = Math.Min(9, i * 10 / members.Count);
    }

    /// <summary>Rows without deciles (a symbol batch can be built on its own; deciles need the whole day).</summary>
    public static (List<GapperStudyUniverseDay> Rows, UniverseBuildStats Stats) BuildRows(
        GapperStudyParams p,
        IReadOnlyDictionary<string, IReadOnlyList<StudyDailyBar>> dailyBySymbol,
        IReadOnlyDictionary<string, IReadOnlyList<SplitEvent>> splitsBySymbol,
        IReadOnlyDictionary<string, string> assetNames,
        DateOnly from, DateOnly to,
        Func<DateOnly, string> splitOf)
    {
        var u = p.Universe;
        int lookback = p.Features.Adv20Sessions;
        var days = MarketCalendar.TradingDays(from, to);
        var rows = new List<GapperStudyUniverseDay>();
        var excluded = new Dictionary<string, int>(StringComparer.Ordinal);
        int reused = 0, universeDays = 0, candidates = 0;

        foreach (var (symbol, barsIn) in dailyBySymbol)
        {
            var bars = barsIn.OrderBy(b => b.DateEt).ToList();
            if (bars.Count == 0) continue;
            if (HasReusedTickerGap(bars, u.ReusedTickerGapSessions)) reused++;
            var splits = splitsBySymbol.TryGetValue(symbol, out var s) ? s.OrderBy(x => x.ExDate).ToList() : [];
            var isAdr = assetNames.TryGetValue(symbol, out var name) && IsAdr(name, u.AdrNamePatterns);
            var byDate = new Dictionary<DateOnly, StudyDailyBar>();
            foreach (var b in bars) byDate[b.DateEt] = b;

            var firstDay = bars[0].DateEt;
            var lastDay = bars[^1].DateEt.AddDays(u.SplitExclusionWindowDays.Before + 10);
            foreach (var d in days)
            {
                if (d <= firstDay || d > lastDay) continue;
                var window = MarketCalendar.PriorTradingDays(d, lookback);
                var present = new List<StudyDailyBar>(lookback);
                foreach (var day in window)
                    if (byDate.TryGetValue(day, out var b)) present.Add(b);
                if (present.Count < u.MinSessionsOfLast20) continue;

                var last = present[^1];
                var factor = SplitFactor(splits, last.DateEt, d);
                var adjPrevClose = last.Close * factor;
                if (adjPrevClose < u.PriceMinInclusive || adjPrevClose >= u.PriceMaxExclusive) continue;
                decimal dollars = 0, shares = 0;
                foreach (var b in present)
                {
                    dollars += b.Close * b.Volume;
                    shares += b.Volume / SplitFactor(splits, b.DateEt, d);
                }
                var adv20Dollars = dollars / present.Count;
                var adv20Shares = shares / present.Count;
                if (adv20Dollars < u.Adv20DollarMin) continue;

                var row = new GapperStudyUniverseDay
                {
                    DateEt = d, Symbol = symbol, Split = splitOf(d),
                    AdjPrevClose = decimal.Round(adjPrevClose, 6), Adv20Dollars = decimal.Round(adv20Dollars, 2),
                    Adv20Shares = decimal.Round(adv20Shares, 2), Sessions = present.Count, IsAdr = isAdr,
                    PriceTier = p.PriceTierOf(adjPrevClose),
                };

                var windowStart = d.AddDays(-u.SplitExclusionWindowDays.Before);
                var windowEnd = d.AddDays(u.SplitExclusionWindowDays.After);
                if (splits.Any(x => x.ExDate >= windowStart && x.ExDate <= windowEnd))
                    row.ExcludedReason = ExcludedSplitWindow;
                else if (HasUnexplainedMove(bars, byDate, splits, d, u.UnadjustedSplitGuard))
                    row.ExcludedReason = ExcludedUnexplainedMove;

                if (byDate.TryGetValue(d, out var today))
                {
                    row.DayHigh = today.High;
                    row.DayVolume = today.Volume;
                    var c = p.Candidate;
                    row.IsCandidate = row.ExcludedReason is null
                        && ((double)(today.High / adjPrevClose - 1m) >= c.HighOverPrevCloseMin
                            || (adv20Shares > 0 && (double)(today.Volume / adv20Shares) >= c.VolumeOverAdv20Min));
                }
                if (row.ExcludedReason is { } reason) excluded[reason] = excluded.GetValueOrDefault(reason) + 1;
                else
                {
                    universeDays++;
                    if (row.IsCandidate) candidates++;
                }
                rows.Add(row);
            }
        }
        return (rows, new UniverseBuildStats(dailyBySymbol.Count, reused, universeDays, candidates, excluded));
    }

    /// <summary>Price multiplier that puts a bar dated <paramref name="barDate"/> on the basis of <paramref name="asOf"/>: every split with ex-date in (barDate, asOf].</summary>
    public static decimal SplitFactor(IReadOnlyList<SplitEvent> splits, DateOnly barDate, DateOnly asOf)
    {
        decimal f = 1m;
        foreach (var s in splits)
            if (s.ExDate > barDate && s.ExDate <= asOf && s.OldRate > 0 && s.NewRate > 0) f *= s.PriceFactor;
        return f;
    }

    public static bool IsAdr(string? name, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var token in name.Split(new[] { ' ', ',', '.', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries))
            foreach (var pattern in patterns)
                if (string.Equals(token, pattern, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool HasUnexplainedMove(List<StudyDailyBar> bars, Dictionary<DateOnly, StudyDailyBar> byDate,
        List<SplitEvent> splits, DateOnly d, GapperStudyParams.UnadjustedSplitGuardParams guard)
    {
        var start = d.AddDays(-guard.LookbackDays);
        for (int i = 1; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (bar.DateEt < start) continue;
            if (bar.DateEt > d) break;
            var prev = bars[i - 1];
            if (prev.Close <= 0) continue;
            var move = (double)(bar.Close / prev.Close - 1m);
            if (move <= guard.MaxDailyUp && move >= guard.MaxDailyDown) continue;
            if (splits.Any(s => s.ExDate > prev.DateEt && s.ExDate <= bar.DateEt)) continue;
            return true;
        }
        return false;
    }

    private static bool HasReusedTickerGap(List<StudyDailyBar> bars, int gapSessions)
    {
        for (int i = 1; i < bars.Count; i++)
        {
            var calendarGap = bars[i].DateEt.DayNumber - bars[i - 1].DateEt.DayNumber;
            if (calendarGap <= gapSessions) continue;
            if (MarketCalendar.TradingDays(bars[i - 1].DateEt.AddDays(1), bars[i].DateEt.AddDays(-1)).Count > gapSessions) return true;
        }
        return false;
    }
}
