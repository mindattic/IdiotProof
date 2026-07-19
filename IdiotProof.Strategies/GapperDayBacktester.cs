using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies;

/// <summary>One simulated fill in a gapper day replay.</summary>
public sealed record GapperBacktestFill(DateTime Utc, double Price, string Reason);

/// <summary>Outcome of replaying one giveback setting over the same day (the hindsight grid).</summary>
public sealed record GivebackGridRow(double GivebackPercent, double ExitPrice, double PnL, string ExitReason);

/// <summary>
/// The full replay of one gapper profile over one past trading day.
/// </summary>
public sealed class GapperDayBacktestReport
{
    public string Symbol { get; init; } = "";
    public DateOnly DayEt { get; init; }
    public bool Entered { get; init; }
    public string? NoEntryReason { get; init; }

    public GapperBacktestFill? Entry { get; init; }
    public GapperBacktestFill? Exit { get; init; }
    public int Quantity { get; init; }
    public double PnL { get; init; }
    public double ReturnPercent { get; init; }

    /// <summary>Gap over previous close at the entry bar, %.</summary>
    public double GapAtEntryPercent { get; init; }

    /// <summary>Highest price between entry and the hard sell-by (max favorable excursion).</summary>
    public double PeakPrice { get; init; }
    public DateTime PeakUtc { get; init; }
    public double MaxFavorablePercent { get; init; }

    /// <summary>Lowest price between entry and exit (max adverse excursion).</summary>
    public double TroughPrice { get; init; }
    public double MaxAdversePercent { get; init; }

    /// <summary>What each giveback setting would have done on this same day.</summary>
    public IReadOnlyList<GivebackGridRow> GivebackGrid { get; init; } = [];

    /// <summary>Plain-English hindsight ("how could it have been better").</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>
    /// The dialed-in profile for reuse on a real trading day: the input
    /// profile with the hindsight-best giveback and a stop informed by the
    /// day's actual adverse excursion. A suggestion, never auto-applied.
    /// </summary>
    public GapperProfile? TunedProfile { get; init; }
}

/// <summary>
/// Replays one gapper profile over one past day's bars — premarket included —
/// answering "what WOULD have happened". Fidelity rule: entries walk the SAME
/// condition list the Monitor walks, and exits run the SAME
/// <see cref="GapperExitEvaluator"/> the live console runs, bar by bar. The
/// only simulation liberty is fill price = the decision bar's close (the live
/// Monitor places marketable limits at current price, so this is the honest
/// analogue).
///
/// Pure: (definition, day bars, previous close) → report. No I/O, no clocks —
/// the caller fetches data (Alpaca for real days, Mock for rehearsal).
/// </summary>
public static class GapperDayBacktester
{
    private static readonly double[] GivebackGridValues = [10, 15, 20, 25, 30, 35, 40, 50];

    public static GapperDayBacktestReport Run(
        string symbol,
        GapperProfile profile,
        IReadOnlyList<Candle> dayCandles,
        decimal? previousClose,
        DateOnly dayEt)
    {
        var def = GapperScriptFactory.Compose(symbol, profile).Build();
        var candles = dayCandles.OrderBy(c => c.StartUtc).ToList();

        if (candles.Count == 0)
            return NoEntry(symbol, dayEt, "No bars for that day — market holiday, bad symbol, or the data feed returned nothing.");
        if (previousClose is null)
            return NoEntry(symbol, dayEt, "No previous close available — gap math is undefined for this day (gap conditions fail closed, same as live).");

        // ── Entry scan: same AND-walk the Monitor runs, same snapshots —
        // including the same pre-computed EMA periods (an EMA-conditioned
        // strategy replayed against empty EMAs would never enter). ──
        var emas = EmaPeriodCollector.Collect(def);
        int entryIndex = -1;
        string? lastBlocker = null;
        for (var i = 0; i < candles.Count; i++)
        {
            var window = candles.Take(i + 1).ToList();
            var snapshot = IndicatorSnapshotBuilder.BuildWithEmas(symbol, window, emas, previousClose);

            var allPass = true;
            foreach (var cond in def.EntryConditions)
            {
                if (!cond.Evaluate(snapshot)) { allPass = false; lastBlocker = cond.ToScript(); break; }
            }
            if (allPass) { entryIndex = i; break; }
        }

        if (entryIndex < 0)
            return NoEntry(symbol, dayEt,
                $"Conditions never all passed. Last blocker: {lastBlocker ?? "(none evaluated)"}. " +
                "Typical causes: the gap never reached your minimum, volume never burst past the ratio, or price sat outside the band.");

        var entryBar = candles[entryIndex];
        var entryPrice = (double)entryBar.Close;
        var entryUtc = entryBar.EndUtc;
        var quantity = Math.Max(1, (int)Math.Floor((double)profile.DefaultNotional / entryPrice));
        var gapAtEntry = (entryPrice - (double)previousClose.Value) / (double)previousClose.Value * 100.0;

        // ── Exit replay: the live brain, bar by bar ──
        var (exit, _) = ReplayExit(def, entryPrice, entryUtc, candles);

        // ── Excursions ── trough (MAE) runs entry→exit; the peak runs
        // entry→hard sell-by (or end of data) so hindsight can see a peak the
        // exit missed — bounding BOTH at the exit made "the peak came AFTER
        // your exit" unobservable and understated the day's real MFE.
        var exitUtc = exit.Utc;
        double peak = entryPrice, trough = entryPrice;
        DateTime peakUtc = entryUtc;
        foreach (var c in candles)
        {
            if (c.EndUtc <= entryUtc) continue;
            if (def.ExitTime is { } sellByEt && MarketTime.ToEasternTimeOfDay(c.EndUtc) > sellByEt) break;
            if ((double)c.High > peak) { peak = (double)c.High; peakUtc = c.EndUtc; }
            if (c.StartUtc <= exitUtc && (double)c.Low < trough) trough = (double)c.Low;
        }

        var pnl = (exit.Price - entryPrice) * quantity;

        // ── Hindsight grid: what would other giveback dials have done? ──
        var grid = new List<GivebackGridRow>();
        foreach (var gb in GivebackGridValues)
        {
            var alt = ClonedDefWithGiveback(symbol, profile, gb);
            var (altExit, _) = ReplayExit(alt, entryPrice, entryUtc, candles);
            grid.Add(new GivebackGridRow(gb, altExit.Price, (altExit.Price - entryPrice) * quantity, altExit.Reason));
        }
        var best = grid.OrderByDescending(g => g.PnL).First();

        var suggestions = BuildSuggestions(profile, entryPrice, exit, peak, peakUtc, trough, pnl, best, quantity);
        var tuned = BuildTunedProfile(profile, best, entryPrice, trough, gapAtEntry);

        return new GapperDayBacktestReport
        {
            Symbol = symbol.ToUpperInvariant(),
            DayEt = dayEt,
            Entered = true,
            Entry = new GapperBacktestFill(entryUtc, entryPrice, "Entry — all conditions passed"),
            Exit = exit,
            Quantity = quantity,
            PnL = pnl,
            ReturnPercent = entryPrice > 0 ? pnl / (entryPrice * quantity) * 100.0 : 0,
            GapAtEntryPercent = gapAtEntry,
            PeakPrice = peak,
            PeakUtc = peakUtc,
            MaxFavorablePercent = (peak - entryPrice) / entryPrice * 100.0,
            TroughPrice = trough,
            MaxAdversePercent = (trough - entryPrice) / entryPrice * 100.0,
            GivebackGrid = grid,
            Suggestions = suggestions,
            TunedProfile = tuned,
        };
    }

    /// <summary>Runs the LIVE exit evaluator over the bars until it trips.</summary>
    private static (GapperBacktestFill Exit, int ExitIndex) ReplayExit(
        StrategyDefinition def, double entryPrice, DateTime entryUtc, List<Candle> candles)
    {
        for (var j = 0; j < candles.Count; j++)
        {
            if (candles[j].EndUtc <= entryUtc) continue;
            var seen = candles.Take(j + 1).ToList();
            var decision = GapperExitEvaluator.Evaluate(def, entryPrice, entryUtc, seen, candles[j].EndUtc);
            if (decision is not null)
                return (new GapperBacktestFill(candles[j].EndUtc, decision.CurrentPrice, decision.Reason.ToString()), j);
        }
        var last = candles[^1];
        return (new GapperBacktestFill(last.EndUtc, (double)last.Close, "EndOfData"), candles.Count - 1);
    }

    private static StrategyDefinition ClonedDefWithGiveback(string symbol, GapperProfile profile, double giveback)
    {
        var p = profile.Clone();
        p.PeakGivebackPercent = giveback;
        return GapperScriptFactory.Compose(symbol, p).Build();
    }

    private static List<string> BuildSuggestions(
        GapperProfile profile, double entryPrice, GapperBacktestFill exit,
        double peak, DateTime peakUtc, double trough, double pnl, GivebackGridRow best, int quantity)
    {
        var s = new List<string>();
        var peakEt = MarketTime.ToEasternTimeOfDay(peakUtc);

        s.Add($"The run peaked at {peak:F2} ({(peak - entryPrice) / entryPrice * 100:+0.0}% over entry) at {peakEt:hh\\:mm} ET; " +
              $"you exited at {exit.Price:F2} ({exit.Reason}), keeping {(peak > entryPrice ? (exit.Price - entryPrice) / (peak - entryPrice) * 100 : 0):F0}% of the peak move.");

        if (best.PnL > pnl + 0.01)
            s.Add($"A peak-giveback of {best.GivebackPercent:F0}% would have exited at {best.ExitPrice:F2} " +
                  $"({best.ExitReason}) for {best.PnL - pnl:+0.00} more P&L than your {profile.PeakGivebackPercent:F0}% setting.");
        else
            s.Add($"Your giveback setting ({profile.PeakGivebackPercent:F0}%) was already the best of the tested grid on this day.");

        var maePct = (trough - entryPrice) / entryPrice * 100.0;
        if (maePct > -(profile.StopLossPercent / 2.0))
            s.Add($"Worst drawdown after entry was only {maePct:F1}% — your {profile.StopLossPercent:F0}% stop was never close. " +
                  $"A tighter stop (~{Math.Max(1, Math.Ceiling(-maePct) + 1):F0}%) would have protected the same trade with less risk.");
        else if (exit.Reason == nameof(GapperExitReason.StopLoss))
            s.Add($"The stop fired at {exit.Price:F2}. Worst drawdown was {maePct:F1}% — if the thesis was right, a wider stop or later entry would have survived the shakeout; if not, the stop did its job.");

        if (peakUtc > exit.Utc)
            s.Add("The peak came AFTER your exit — this day rewarded more patience (later arm time or larger giveback).");

        s.Add($"Position: {quantity} shares (${profile.DefaultNotional} notional at {entryPrice:F2}).");
        return s;
    }

    private static GapperProfile BuildTunedProfile(
        GapperProfile profile, GivebackGridRow best, double entryPrice, double trough, double gapAtEntry)
    {
        var tuned = profile.Clone();
        // Idempotent suffix: re-tuning an already-tuned profile must not stack
        // "(tuned) (tuned)" onto the name.
        tuned.Name = profile.Name.EndsWith(" (tuned)", StringComparison.Ordinal)
            ? profile.Name
            : $"{profile.Name} (tuned)";
        tuned.PeakGivebackPercent = best.GivebackPercent;

        // Stop informed by the day's real adverse excursion, +1% cushion,
        // clamped to sane bounds and never LOOSENED beyond the original.
        var maePct = Math.Abs(Math.Min(0, (trough - entryPrice) / entryPrice * 100.0));
        var suggestedStop = Math.Clamp(Math.Ceiling(maePct) + 1, 1, 15);
        tuned.StopLossPercent = Math.Min(profile.StopLossPercent, suggestedStop);

        // Gap screen: keep a little under the day's actual entry gap so a
        // repeat of the same setup still qualifies.
        if (gapAtEntry > 0 && gapAtEntry - 1 > tuned.MinGapPercent)
            tuned.MinGapPercent = Math.Floor(gapAtEntry - 1);

        return tuned;
    }

    private static GapperDayBacktestReport NoEntry(string symbol, DateOnly dayEt, string reason) => new()
    {
        Symbol = symbol.ToUpperInvariant(),
        DayEt = dayEt,
        Entered = false,
        NoEntryReason = reason,
    };
}
