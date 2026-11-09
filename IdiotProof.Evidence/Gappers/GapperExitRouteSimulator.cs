using System.Globalization;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>The clock exit routes of strategy-refined.md 2.5 and methods-execution.md 2.5.</summary>
public enum GapperExitRouteKind
{
    /// <summary>Route (a): a marketable extended-hours sell at a premarket clock time (09:15, 09:20).</summary>
    Continuous,
    /// <summary>Route (b): an <c>opg</c> limit sell submitted by 09:27, with a regular-hours fallback at 09:30:05.</summary>
    OpgLimit,
    /// <summary>Route (c) and later: a regular-hours sell at a clock time after the bell (09:31, 09:44, 09:50).</summary>
    Regular,
}

/// <summary>
/// One clock exit. <see cref="OpgLimitDiscount"/> is x in <c>L = last x (1 - x)</c>; <see cref="LuldAware"/> sells at
/// 09:44 when the last three minutes moved more than 0.8 of the post-09:45 LULD band (a route held past 09:45 only).
/// </summary>
public sealed record GapperExitRoute(string Name, GapperExitRouteKind Kind, TimeSpan AtEt, double OpgLimitDiscount = 0, bool LuldAware = false)
{
    /// <summary>Alpaca rejects an opg order submitted after 09:28 (and before 19:00).</summary>
    public static readonly TimeSpan OpgCutoffEt = new(9, 28, 0);
    /// <summary>The fallback regular-hours sell when the opg order comes back unfilled.</summary>
    public static readonly TimeSpan OpgFallbackEt = new(9, 30, 5);
    /// <summary>The LULD-aware check and exit minute: before the 09:45 band contraction.</summary>
    public static readonly TimeSpan LuldExitEt = new(9, 44, 0);
    /// <summary>LULD bands are doubled from 09:30 until this time, then contract.</summary>
    public static readonly TimeSpan BandContractionEt = new(9, 45, 0);
    /// <summary>The share of the post-09:45 band the last three minutes' range must exceed to trigger the LULD exit.</summary>
    public const double LuldBandShare = 0.8;

    public static GapperExitRoute Continuous(TimeSpan atEt)
    {
        if (atEt >= MinuteBarMath.RthOpenEt) throw new ArgumentOutOfRangeException(nameof(atEt), "A continuous premarket sell must be before 09:30.");
        return new($"continuous {atEt:hh\\:mm}", GapperExitRouteKind.Continuous, atEt);
    }

    /// <param name="discount">x in L = last x (1 - x).</param>
    /// <param name="submitEt">When the opg order is sent (default 09:27); at or after 09:28 Alpaca rejects it.</param>
    public static GapperExitRoute Opg(double discount, TimeSpan? submitEt = null)
    {
        var at = submitEt ?? new TimeSpan(9, 27, 0);
        if (at >= OpgCutoffEt) throw new ArgumentOutOfRangeException(nameof(submitEt), "Alpaca rejects opg orders submitted at or after 09:28.");
        if (discount is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(discount), "The opg limit discount must be in [0, 1).");
        return new(string.Create(CultureInfo.InvariantCulture, $"opg limit -{discount * 100:0.#}% by {at:hh\\:mm}, fallback 09:30:05"), GapperExitRouteKind.OpgLimit, at, discount);
    }

    public static GapperExitRoute Regular(TimeSpan atEt, bool luldAware = false)
    {
        if (atEt <= MinuteBarMath.RthOpenEt) throw new ArgumentOutOfRangeException(nameof(atEt), "A regular-hours sell must be after 09:30.");
        bool aware = luldAware && atEt > LuldExitEt;
        return new($"regular {atEt:hh\\:mm}{(aware ? " with LULD 09:44 exit" : "")}", GapperExitRouteKind.Regular, atEt, 0, aware);
    }

    /// <summary>The window whose volume the exit sells into (participation cap): the 5 minutes before a premarket sell, 09:20-09:27 for opg, the minute before to 4 after a regular sell.</summary>
    public (TimeSpan From, TimeSpan To) ExitWindowEt => Kind switch
    {
        GapperExitRouteKind.Continuous => (AtEt - TimeSpan.FromMinutes(5), AtEt),
        GapperExitRouteKind.OpgLimit => (new TimeSpan(9, 20, 0), AtEt),
        _ => (AtEt - TimeSpan.FromMinutes(1), AtEt + TimeSpan.FromMinutes(4)),
    };

    /// <summary>Overlays may signal on minute ends strictly before this time; at it the clock route takes over.</summary>
    public TimeSpan OverlayCutoffEt => AtEt;
}

/// <summary>
/// The guards and early-exit overlays riding on a clock route. The stop is the Monitor's close-based guard (no broker
/// stop exists before 09:30): a close at or under entry x (1 - s) sells at the next bar's open. The overlays only sell
/// earlier than the clock. With <see cref="UseExhaustionState"/>, the earliest of the first trade-count collapse after
/// entry (unless <see cref="TradesCollapseFromBars"/> is off) and <see cref="ExternalCollapseUtc"/> (a trade-flow
/// imbalance collapse, or a collapse the caller already computed) is handed to both overlays.
/// </summary>
public sealed record GapperExitOverlays(
    double StopLossPct = 10,
    DriftChangeExitRule? DriftChange = null,
    GivebackExitRule? Giveback = null,
    bool UseExhaustionState = false,
    int CollapseWindowMinutes = 5,
    double CollapseTradesRoc = ExhaustionState.DefaultTradesRocThreshold,
    DateTime? ExternalCollapseUtc = null,
    bool TradesCollapseFromBars = true)
{
    public static readonly GapperExitOverlays ClockOnly = new();
}

/// <param name="ExitReason">stop, drift-change, giveback, luld-band, sell-by, opg, opg-miss fallback, opg no-cross fallback.</param>
/// <param name="HaltFill">The exit filled at the first bar after a gap (no bar in the signal minute): the halt fill rule.</param>
/// <param name="OpgFilled">For the opg route: whether the opening print satisfied the limit; null for other routes or an earlier exit.</param>
public sealed record GapperRouteTrade(DateTime EntryUtc, decimal Entry, DateTime ExitUtc, decimal Exit, string ExitReason, double GrossReturn, bool HaltFill, bool? OpgFilled, DateTime? CollapseUtc)
{
    public double Net(double roundTripCost) => GrossReturn - roundTripCost;
}

/// <summary>
/// Replays one long gapper position from its entry to one clock route, with the guard and overlays (IP-T471). Fill
/// rules are conservative and use minute bars only:
/// - a guard or overlay signal at a minute end, and a continuous or regular clock sell, fills at the open of the
///   first real bar starting at or after it; when that bar is later than the signal minute the fill is a halt fill
///   (the frozen rule: the first post-halt open, the reopening price, not the pre-halt one);
/// - the opg limit L = close as of 09:27 x (1 - x) fills at the opening print (the 09:30 bar's open, until the
///   auction prints are pulled) only when the print trades through it (strictly above L); otherwise the 09:30:05
///   fallback sells at the worse of the 09:30 bar's open and close, and with no 09:30 bar (no cross) at the first
///   post-gap open;
/// - the LULD-aware exit checks at 09:44 whether the last three minutes' range exceeds 0.8 of the post-09:45 band
///   around the mean of the last five closes, and if so sells at the 09:44 bar.
/// Gross only: the caller nets the frozen cost model (it depends on the size). Pure.
/// </summary>
public static class GapperExitRouteSimulator
{
    public static GapperRouteTrade? Simulate(GapperExitRoute route, GapperExitOverlays overlays, IReadOnlyList<Candle> dayBars, DateOnly dateEt, DateTime entryBarStartUtc, decimal entryPrice)
    {
        if (entryPrice <= 0) return null;
        var real = dayBars.Where(b => b.Volume > 0 && b.Open > 0).OrderBy(b => b.StartUtc).ToList();
        if (real.Count == 0) return null;
        var clockUtc = MinuteBarMath.ToUtc(dateEt, route.AtEt);
        if (entryBarStartUtc >= clockUtc) return null;
        var cutoffUtc = MinuteBarMath.ToUtc(dateEt, route.OverlayCutoffEt);

        // Exhaustion state: computed from entry, independent of the overlays' own state.
        DateTime? collapse = null;
        if (overlays.UseExhaustionState)
            collapse = ExhaustionState.Earliest(
                overlays.TradesCollapseFromBars ? ExhaustionState.TradesCollapse(real, entryBarStartUtc, cutoffUtc, overlays.CollapseWindowMinutes, overlays.CollapseTradesRoc) : null,
                overlays.ExternalCollapseUtc is { } ext && ext > entryBarStartUtc && ext <= cutoffUtc ? ext : null);

        // Every candidate signal is a minute end strictly before the clock cutoff; the earliest one exits.
        var signals = new List<(DateTime At, string Reason)>();
        var stop = entryPrice * (1m - (decimal)(overlays.StopLossPct / 100.0));
        foreach (var b in MinuteBarMath.ForwardFill(real, entryBarStartUtc, cutoffUtc))
            if (b.Close <= stop) { signals.Add((b.EndUtc, "stop")); break; }
        var lastSignal = cutoffUtc.AddMinutes(-1);
        if (overlays.DriftChange is { } drift && drift.Scan(real, entryBarStartUtc, lastSignal, collapse) is { Exit: true, SignalUtc: { } dAt })
            signals.Add((dAt, "drift-change"));
        if (overlays.Giveback is { } give && give.Scan(real, entryBarStartUtc, lastSignal, collapse) is { Exit: true, SignalUtc: { } gAt })
            signals.Add((gAt, "giveback"));
        if (route.LuldAware)
        {
            var check = MinuteBarMath.ToUtc(dateEt, GapperExitRoute.LuldExitEt);
            if (check > entryBarStartUtc && check < cutoffUtc && BandContractionRisk(real, check)) signals.Add((check, "luld-band"));
        }
        var first = signals.Where(s => s.At < cutoffUtc).OrderBy(s => s.At).FirstOrDefault();
        if (first.Reason is not null) return Fill(real, first.At, first.Reason, entryBarStartUtc, entryPrice, null, collapse);

        if (route.Kind != GapperExitRouteKind.OpgLimit)
            return Fill(real, clockUtc, $"sell-by {route.AtEt:hh\\:mm}", entryBarStartUtc, entryPrice, null, collapse);

        // Route (b): the opg limit from the last close as of the submit time, then the opening print or the fallback.
        if (MinuteBarMath.CloseAsOf(real, clockUtc) is not { } last || last <= 0)
            return Fill(real, clockUtc, "opg not sent (no price)", entryBarStartUtc, entryPrice, null, collapse);
        var limit = last * (1m - (decimal)route.OpgLimitDiscount);
        var openUtc = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        var openBar = real.FirstOrDefault(b => b.StartUtc == openUtc);
        if (openBar is not null && openBar.Open > limit)
            return Trade(entryBarStartUtc, entryPrice, openBar.StartUtc, openBar.Open, "opg", false, true, collapse);
        if (openBar is not null)
            return Trade(entryBarStartUtc, entryPrice, openBar.StartUtc, Math.Min(openBar.Open, openBar.Close), "opg-miss fallback 09:30:05", false, false, collapse);
        return Fill(real, openUtc, "opg no-cross fallback", entryBarStartUtc, entryPrice, false, collapse);
    }

    /// <summary>
    /// The post-09:45 LULD band as a fraction of the reference price, Tier 2 (small caps): 10% at $3 and up, 20% from
    /// $0.75 to $3, and below $0.75 the lesser of $0.15 or 75%.
    /// </summary>
    public static double LuldBandFraction(double referencePrice)
    {
        if (!(referencePrice > 0)) return double.NaN;
        if (referencePrice >= 3) return 0.10;
        if (referencePrice >= 0.75) return 0.20;
        return Math.Min(0.15 / referencePrice, 0.75);
    }

    /// <summary>
    /// True when the high-low range of the three minutes ending at <paramref name="atUtc"/> exceeds
    /// <see cref="GapperExitRoute.LuldBandShare"/> of the post-09:45 band around the mean of the last five closes:
    /// a name moving this fast would likely pause once the bands contract.
    /// </summary>
    public static bool BandContractionRisk(IReadOnlyList<Candle> bars, DateTime atUtc, double share = GapperExitRoute.LuldBandShare)
    {
        var last5 = bars.Where(b => b.Volume > 0 && b.EndUtc <= atUtc && b.EndUtc > atUtc.AddMinutes(-5)).ToList();
        var last3 = last5.Where(b => b.EndUtc > atUtc.AddMinutes(-3)).ToList();
        if (last3.Count == 0) return false;
        double reference = (double)last5.Average(b => b.Close);
        double band = LuldBandFraction(reference) * reference;
        double range = (double)(last3.Max(b => b.High) - last3.Min(b => b.Low));
        return double.IsFinite(band) && range > share * band;
    }

    private static GapperRouteTrade Fill(List<Candle> real, DateTime signalUtc, string reason, DateTime entryUtc, decimal entry, bool? opg, DateTime? collapse)
    {
        var bar = real.FirstOrDefault(b => b.StartUtc >= signalUtc);
        if (bar is null)
        {
            var last = real.LastOrDefault(b => b.StartUtc >= entryUtc) ?? real[^1];
            return Trade(entryUtc, entry, last.StartUtc, last.Close, reason + " (no later bar: last close)", true, opg, collapse);
        }
        return Trade(entryUtc, entry, bar.StartUtc, bar.Open, reason, bar.StartUtc > signalUtc, opg, collapse);
    }

    private static GapperRouteTrade Trade(DateTime entryUtc, decimal entry, DateTime exitUtc, decimal exit, string reason, bool halt, bool? opg, DateTime? collapse) =>
        new(entryUtc, entry, exitUtc, exit, reason, entry > 0 ? (double)(exit / entry - 1m) : 0, halt, opg, collapse);
}
