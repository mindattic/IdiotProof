namespace IdiotProof.Core.Research.Momentum;

/// <summary>One 1-minute bar in ET with the session VWAP (from 04:00) as of its close.</summary>
public sealed record ScenarioBar(DateTime StartUtc, TimeSpan Et, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal Vwap);

/// <summary>A level in a plan: a price, or VWAP (moves bar by bar).</summary>
public sealed record ScenarioLevel(decimal? Price, bool Vwap = false)
{
    public decimal? At(ScenarioBar b) => Vwap ? b.Vwap : Price;
    public override string ToString() => Vwap ? "VWAP" : Price?.ToString("0.00##", System.Globalization.CultureInfo.InvariantCulture) ?? "?";
}

/// <summary>
/// A plan as he states it (IP-T493). <see cref="TriggerKind"/>: <c>break</c> (a trade over the level),
/// <c>reclaim</c> (trades under it, then a 1-minute close back over it) or <c>hold</c> (pulls into it, holds,
/// then a close over that pullback bar's high: his "flush ... confirm a higher low over 250").
/// <see cref="Confirm"/> are the pullback levels he wants held after the trigger ("confirm pullbacks back
/// over 320, VWAP"); with <see cref="RequiresConfirm"/> the plan enters only on such a pullback.
/// </summary>
public sealed record ScenarioSpec(
    string TriggerKind,
    ScenarioLevel Trigger,
    IReadOnlyList<ScenarioLevel> Confirm,
    IReadOnlyList<decimal> Targets,
    decimal? Invalidation,
    bool RequiresConfirm);

public sealed record TargetHit(decimal Level, DateTime? HitUtc);

public sealed record ScenarioResult
{
    public string Verdict { get; init; } = MomentumVerdicts.Unverifiable;
    public bool TriggerHit { get; init; }
    public DateTime? TriggerUtc { get; init; }
    public decimal? TriggerPrice { get; init; }
    public bool TriggerGapThrough { get; init; }
    public bool? ConfirmHit { get; init; }
    public decimal? ConfirmLevel { get; init; }
    public DateTime? ConfirmUtc { get; init; }
    public decimal? EntryPrice { get; init; }
    public DateTime? EntryUtc { get; init; }
    public IReadOnlyList<TargetHit> Targets { get; init; } = [];
    public bool FirstTargetHit { get; init; }
    public DateTime? FirstTargetUtc { get; init; }
    public bool AnyTargetHit => Targets.Any(t => t.HitUtc is not null);
    public int TargetsHit => Targets.Count(t => t.HitUtc is not null);
    public decimal? InvalidationLevel { get; init; }
    public string? InvalidationSource { get; init; }
    public bool InvalidationHitBeforeTarget { get; init; }
    public double? MaxRunAfterTriggerPct { get; init; }
    public double? MaxDrawdownAfterTriggerPct { get; init; }
    public double? CloseVsTriggerPct { get; init; }
    public decimal? ExitPrice { get; init; }
    public DateTime? ExitUtc { get; init; }
    public string? ExitReason { get; init; }
    public double? PlanPnlPct { get; init; }
    public string? Note { get; init; }
}

public static class MomentumVerdicts
{
    public const string TargetHit = "target-hit";
    public const string TriggeredNoTarget = "triggered-no-target";
    public const string Stopped = "stopped";
    public const string NotTriggered = "not-triggered";
    public const string Unverifiable = "unverifiable";
    public const string BragAccurate = "brag-accurate";
    public const string BragExaggerated = "brag-exaggerated";
    public const string NotEvaluated = "not-evaluated";
}

/// <summary>
/// Plays one stated plan over a day of 1-minute bars (IP-T493), deterministically:
/// <list type="bullet">
/// <item>Trigger from 04:00 to 15:55 ET. <c>break</c>: the first bar whose high is over the level; the trigger price is
/// the level, or the bar's open when it opened over it (gap-through, noted).</item>
/// <item>Confirm (when stated): after the trigger, the first bar that pulls to within 2% of a confirm level at or under
/// the trigger price and closes at or over it; entry at that close. Without a stated confirm, entry is the trigger price.</item>
/// <item>Targets: stated targets over the trigger level, ascending; a target is hit when a bar's high reaches it, from
/// the trigger bar on.</item>
/// <item>Invalidation: the stated level, else the lowest numeric confirm level, else VWAP when that was the confirm,
/// else the trigger level; it is hit by a 1-minute close more than 2% under it (<see cref="StopBuffer"/>).</item>
/// <item>Plan P&amp;L: from the entry, exit at the first target, at a close under the invalidation, or at the 15:55 bar's
/// close, whichever comes first (a bar that reaches the target and closes under the stop counts as the target).</item>
/// </list>
/// Verdict: an entered plan by its exit (target-hit, stopped, triggered-no-target at 15:55); a plan whose pullback entry never
/// came by the trigger-level path (first target reached, else a stop before it, else neither).
/// Run and drawdown are measured from the trigger price over 04:00-16:00 bars after the trigger.
/// </summary>
public static class MomentumScenarioEvaluator
{
    public static readonly TimeSpan LastEntry = new(15, 55, 0);
    public static readonly TimeSpan RthOpen = new(9, 30, 0);
    public static readonly TimeSpan RthClose = new(16, 0, 0);
    public const decimal PullbackBand = 0.02m;
    public const decimal HoldBand = 0.03m;
    /// <summary>A stop needs a 1-minute close more than 2% under the invalidation level: a close a cent under it is noise on these names.</summary>
    public const decimal StopBuffer = 0.02m;

    /// <param name="notBeforeUtc">When the call was made on the session itself (a morning update): triggers count from this
    /// minute on; earlier bars still feed VWAP and a reclaim's "traded under the level".</param>
    public static ScenarioResult Evaluate(ScenarioSpec spec, IReadOnlyList<ScenarioBar> bars, DateTime? notBeforeUtc = null)
    {
        var day = bars.Where(b => b.Et < RthClose).OrderBy(b => b.StartUtc).ToList();
        if (day.Count == 0) return new ScenarioResult { Verdict = MomentumVerdicts.Unverifiable, Note = "no bars" };
        if (spec.Trigger.Price is null && !spec.Trigger.Vwap)
            return new ScenarioResult { Verdict = MomentumVerdicts.Unverifiable, Note = "no trigger level" };

        // ── trigger ──
        int ti = -1; decimal tp = 0; bool gapThrough = false;
        var kind = (spec.TriggerKind ?? "break").ToLowerInvariant();
        bool seenBelow = false;
        int holdBar = -1;
        for (int i = 0; i < day.Count && day[i].Et <= LastEntry; i++)
        {
            var b = day[i];
            var lvl = spec.Trigger.At(b);
            if (lvl is not { } L || L <= 0) continue;
            bool live = notBeforeUtc is not { } nb || b.StartUtc >= nb;
            if (kind == "reclaim" || (spec.Trigger.Vwap && kind == "break"))
            {
                if (b.Low < L) seenBelow = true;
                if (live && seenBelow && b.Close > L) { ti = i; tp = b.Close; break; }
            }
            else if (!live) continue;
            else if (kind == "hold")
            {
                // the lowest bar that pulls into the zone (within 3% over the level, or through it) is the pullback;
                // a later close over the level and over that bar's high is the higher low confirmed
                if (b.Low <= L * (1 + HoldBand) && (holdBar < 0 || b.Low <= day[holdBar].Low)) { holdBar = i; continue; }
                if (holdBar >= 0 && b.Close > L && b.Close > day[holdBar].High) { ti = i; tp = b.Close; break; }
            }
            else if (b.High > L)
            {
                ti = i;
                gapThrough = b.Open > L;
                tp = gapThrough ? b.Open : L;
                break;
            }
        }

        var targetsAll = spec.Targets.Where(t => t > 0).Distinct().OrderBy(t => t).ToList();
        if (ti < 0)
            return new ScenarioResult
            {
                Verdict = MomentumVerdicts.NotTriggered,
                Targets = targetsAll.Select(t => new TargetHit(t, null)).ToList(),
            };

        var trig = day[ti];
        var trigLevel = spec.Trigger.At(trig) ?? tp;
        var after = day.Skip(ti).ToList();
        decimal maxHigh = after.Max(b => b.High), minLow = after.Min(b => b.Low);
        var lastClose = day[^1].Close;

        // ── targets (over the trigger level) ──
        var targets = targetsAll.Where(t => t > trigLevel).ToList();
        var hits = new List<TargetHit>();
        foreach (var t in targets)
        {
            var hit = after.FirstOrDefault(b => b.High >= t);
            hits.Add(new TargetHit(t, hit?.StartUtc));
        }
        var firstTargetUtc = hits.Count > 0 ? hits[0].HitUtc : null;

        // ── invalidation ──
        decimal? inval = spec.Invalidation; string invalSource = "stated";
        bool invalVwap = false;
        if (inval is null)
        {
            var numericConfirm = spec.Confirm.Where(c => c.Price is > 0 && c.Price <= trigLevel).Select(c => c.Price!.Value).ToList();
            if (numericConfirm.Count > 0) { inval = numericConfirm.Min(); invalSource = "default-confirm"; }
            else if (spec.Confirm.Any(c => c.Vwap)) { invalVwap = true; invalSource = "default-vwap"; }
            else if (spec.Trigger.Price is { } tpx) { inval = tpx; invalSource = "default-trigger"; }
            else { invalVwap = true; invalSource = "default-vwap"; }
        }
        decimal? InvalAt(ScenarioBar b) => invalVwap ? b.Vwap : inval;

        DateTime? invalUtc = after.Skip(1).FirstOrDefault(b => InvalAt(b) is { } iv && b.Close < iv * (1 - StopBuffer))?.StartUtc;
        bool invalBeforeTarget = invalUtc is { } iu && (firstTargetUtc is null || iu < firstTargetUtc);

        // ── confirm / entry ──
        bool? confirmHit = null; decimal? confirmLevel = null; DateTime? confirmUtc = null;
        int ei = -1; decimal? entry = null; bool sameBarTargets = false;
        if (spec.RequiresConfirm && spec.Confirm.Count > 0)
        {
            // the pullback entry has to come before the first target: a dip after the move already paid is another trade
            confirmHit = false;
            for (int i = ti + 1; i < day.Count && day[i].Et <= LastEntry && (firstTargetUtc is null || day[i].StartUtc < firstTargetUtc); i++)
            {
                var b = day[i];
                foreach (var c in spec.Confirm)
                {
                    if (c.At(b) is not { } cl || cl <= 0 || cl > Math.Max(tp, trigLevel) * 1.001m) continue;
                    if (b.Low <= cl * (1 + PullbackBand) && b.Close >= cl)
                    {
                        confirmHit = true; confirmLevel = cl; confirmUtc = b.StartUtc; ei = i; entry = b.Close;
                        break;
                    }
                }
                if (confirmHit == true) break;
            }
        }
        else
        {
            ei = ti; entry = tp;
            sameBarTargets = kind == "break" && !spec.Trigger.Vwap;
        }

        // ── plan ──
        decimal? exit = null; DateTime? exitUtc = null; string? reason = null; double? pnl = null;
        if (ei >= 0 && entry is { } e && e > 0)
        {
            var t1 = targetsAll.Where(t => t > e).Cast<decimal?>().FirstOrDefault();
            for (int i = sameBarTargets ? ei : ei + 1; i < day.Count; i++)
            {
                var b = day[i];
                if (t1 is { } tt && b.High >= tt) { exit = tt; exitUtc = b.StartUtc; reason = "target"; break; }
                if (i > ei && InvalAt(b) is { } iv && b.Close < iv * (1 - StopBuffer)) { exit = b.Close; exitUtc = b.StartUtc; reason = "stop"; break; }
                if (b.Et >= LastEntry) { exit = b.Close; exitUtc = b.StartUtc; reason = "time"; break; }
            }
            if (exit is null) { exit = day[^1].Close; exitUtc = day[^1].StartUtc; reason = "time"; }
            pnl = Pct(exit.Value, e);
        }
        else if (confirmHit == false) reason = "no-confirm";

        // an entered plan is judged by how it exited; a plan that never got its pullback entry by the levels after the trigger
        bool firstHit = firstTargetUtc is not null;
        var verdict = reason switch
        {
            "target" => MomentumVerdicts.TargetHit,
            "stop" => MomentumVerdicts.Stopped,
            "time" => MomentumVerdicts.TriggeredNoTarget,
            _ => invalBeforeTarget ? MomentumVerdicts.Stopped : firstHit ? MomentumVerdicts.TargetHit : MomentumVerdicts.TriggeredNoTarget,
        };

        return new ScenarioResult
        {
            Verdict = verdict,
            TriggerHit = true,
            TriggerUtc = trig.StartUtc,
            TriggerPrice = tp,
            TriggerGapThrough = gapThrough,
            ConfirmHit = confirmHit,
            ConfirmLevel = confirmLevel,
            ConfirmUtc = confirmUtc,
            EntryPrice = entry,
            EntryUtc = ei >= 0 ? day[ei].StartUtc : null,
            Targets = hits,
            FirstTargetHit = firstHit,
            FirstTargetUtc = firstTargetUtc,
            InvalidationLevel = invalVwap ? null : inval,
            InvalidationSource = invalSource,
            InvalidationHitBeforeTarget = invalBeforeTarget,
            MaxRunAfterTriggerPct = Pct(maxHigh, tp),
            MaxDrawdownAfterTriggerPct = Pct(minLow, tp),
            CloseVsTriggerPct = trigLevel > 0 ? Pct(lastClose, trigLevel) : null,
            ExitPrice = exit,
            ExitUtc = exitUtc,
            ExitReason = reason,
            PlanPnlPct = pnl,
            Note = gapThrough ? "opened over the trigger (gap-through)" : null,
        };
    }

    /// <summary>VWAP from 04:00, bar by bar (typical price x volume).</summary>
    public static IReadOnlyList<ScenarioBar> WithVwap(IEnumerable<(DateTime StartUtc, TimeSpan Et, decimal O, decimal H, decimal L, decimal C, decimal V)> raw)
    {
        decimal pv = 0, vol = 0;
        var list = new List<ScenarioBar>();
        foreach (var r in raw.OrderBy(x => x.StartUtc))
        {
            var typical = (r.H + r.L + r.C) / 3;
            pv += typical * r.V; vol += r.V;
            list.Add(new ScenarioBar(r.StartUtc, r.Et, r.O, r.H, r.L, r.C, r.V, vol > 0 ? pv / vol : r.C));
        }
        return list;
    }

    public static double Pct(decimal a, decimal b) => b == 0 ? 0 : Math.Round((double)(a / b - 1) * 100, 2);
}
