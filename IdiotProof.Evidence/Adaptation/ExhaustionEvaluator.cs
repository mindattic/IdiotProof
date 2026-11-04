using IdiotProof.Evidence.Traps;
using IdiotProof.Models;

namespace IdiotProof.Evidence.Adaptation;

public enum ExhaustionSide { None, Long, Short }

/// <summary>Thresholds for the intraday exhaustion templates. Every probability is a counted rate, never a weight.</summary>
public sealed record ExhaustionOptions
{
    /// <summary>How far price must have travelled from the day's opposite extreme, in ATRs, before an extreme counts as exhaustion.</summary>
    public double MinMoveAtr { get; init; } = 3.0;

    /// <summary>The move is measured over this many recent bars, so a slow drift never reads as exhaustion.</summary>
    public int StretchWindow { get; init; } = 10;

    /// <summary>A reversal "pays" when price moves RewardAtr in favor before RiskAtr against, within Horizon bars.</summary>
    public int Horizon { get; init; } = 15;
    public double RewardAtr { get; init; } = 1.0;
    public double RiskAtr { get; init; } = 1.0;

    /// <summary>Minimum measured reversal rate for the bucket, and the history it must rest on.</summary>
    public double MinReversalProbability { get; init; } = 0.55;
    public int MinSamples { get; init; } = 30;

    /// <summary>Long side: the learned falling-knife hit rate must be at or below this, over at least KnifeMinSamples cases.</summary>
    public double KnifeMaxProbability { get; init; } = 0.5;
    public int KnifeMinSamples { get; init; } = 30;

    /// <summary>Short side: the learned failed-breakout rate (longs punished) must be at least this.</summary>
    public double TrapMinProbability { get; init; } = 0.55;

    public int Bins { get; init; } = 3;
    public InformationGateOptions Gate { get; init; } = new();
}

/// <summary>One exhaustion verdict for one symbol at one moment, with everything it rested on.</summary>
public sealed record ExhaustionDecision(
    ExhaustionSide Side,
    bool Arm,
    string Reason,
    GateDecision Gate,
    double MoveAtr,
    double ExtremePrice,
    double TriggerPrice,
    OutcomeRate? Reversal,
    TrapAssessment? Trap,
    DerivedParameters? Parameters,
    int ClosedBars);

/// <summary>
/// IP-US-S2: at a fresh low-of-day after a large drop, should a mean-reversion long be armed; at a
/// failed new high-of-day after a large run, would a fade short pay? Both answers come from the
/// symbol's own prior sessions: a reversal table counts how often the same kind of extreme (bucketed
/// by how stretched it was) paid RewardAtr before costing RiskAtr, and the trap detectors supply the
/// learned falling-knife (long) or failed-breakout (short) rate. Thin history, an unread detector or
/// an unmet threshold all skip; only bars that had closed by <c>now</c> are read.
/// </summary>
public static class ExhaustionEvaluator
{
    private const int Warmup = 20;

    public static ExhaustionDecision Evaluate(IReadOnlyList<IReadOnlyList<Candle>> priorSessions,
        IReadOnlyList<Candle> today, DateTime periodStartUtc, DateTime nowUtc, ExhaustionOptions? options = null)
    {
        options ??= new ExhaustionOptions();
        var gate = InformationGate.Check(today, periodStartUtc, nowUtc, options.Gate);
        if (!gate.Ready) return Skip(ExhaustionSide.None, $"waiting for information: {gate.Reason}", gate, 0);

        var closed = InformationGate.ClosedBars(today.Where(c => c.StartUtc >= periodStartUtc), nowUtc);
        int last = closed.Count - 1;
        if (last < Warmup) return Skip(ExhaustionSide.None, $"only {closed.Count} closed bars; {Warmup + 1} needed to read ATR and traps", gate, closed.Count);

        var s = new CausalSeries(closed);
        double atr = Math.Max(s.Atr[last], 1e-9);

        if (IsNewLow(s, last, options.MinMoveAtr, out double dropAtr, options.StretchWindow))
            return EvaluateLong(priorSessions, closed, s, last, dropAtr, gate, options);
        if (IsFailedNewHigh(s, last, options.MinMoveAtr, out double riseAtr, out double brokenHigh, options.StretchWindow))
            return EvaluateShort(priorSessions, closed, s, last, riseAtr, brokenHigh, gate, options);

        return Skip(ExhaustionSide.None, "no exhaustion extreme on the last closed bar", gate, closed.Count);
    }

    /// <summary>Bar i set the session's low so far, closing at least minMoveAtr ATRs below the highest high of the last <paramref name="window"/> bars.</summary>
    public static bool IsNewLow(CausalSeries s, int i, double minMoveAtr, out double dropAtr, int window = 10)
    {
        dropAtr = 0;
        if (i < 1) return false;
        if (s.Low(i) > s.LowestLow(0, i - 1)) return false;
        dropAtr = (s.HighestHigh(Math.Max(0, i - window), i) - s.Close(i)) / Math.Max(s.Atr[i], 1e-9);
        return dropAtr >= minMoveAtr;
    }

    /// <summary>
    /// One of the last three bars set the session's high so far, at least minMoveAtr ATRs above the
    /// lowest low of the <paramref name="window"/> bars before it, and bar i closed back below the
    /// high that bar broke: a failed new high-of-day.
    /// </summary>
    public static bool IsFailedNewHigh(CausalSeries s, int i, double minMoveAtr, out double riseAtr, out double brokenHigh, int window = 10)
    {
        riseAtr = 0;
        brokenHigh = 0;
        for (int j = i; j > i - 3 && j >= 1; j--)
        {
            double prior = s.HighestHigh(0, j - 1);
            if (s.High(j) <= prior) continue;
            if (s.Close(i) >= prior) return false;
            riseAtr = (s.High(j) - s.LowestLow(Math.Max(0, j - window), j)) / Math.Max(s.Atr[j], 1e-9);
            brokenHigh = prior;
            return riseAtr >= minMoveAtr;
        }
        return false;
    }

    private static ExhaustionDecision EvaluateLong(IReadOnlyList<IReadOnlyList<Candle>> history, List<Candle> closed,
        CausalSeries s, int last, double dropAtr, GateDecision gate, ExhaustionOptions o)
    {
        var knife = TrapDetectors.FallingKnife(bins: o.Bins);
        knife.Learn(history);
        var knifeRead = knife.Assess(s, last);
        if (knifeRead is not null && !knifeRead.IsClear(o.KnifeMaxProbability, o.KnifeMinSamples))
            return Skip(ExhaustionSide.Long,
                knifeRead.Rate.Samples < o.KnifeMinSamples
                    ? $"falling-knife history too thin (n={knifeRead.Rate.Samples} < {o.KnifeMinSamples})"
                    : $"falling knife: learned hit rate {knifeRead.Rate.Probability:P0} > {o.KnifeMaxProbability:P0} (n={knifeRead.Rate.Samples})",
                gate, closed.Count, dropAtr, s.Low(last), knifeRead);

        var reversal = ReversalRate(history, long_: true, dropAtr, o);
        var verdict = Verdict(reversal, o, "bounce");
        var parameters = AdaptiveParameterDeriver.Derive(closed);
        return new ExhaustionDecision(ExhaustionSide.Long, verdict is null, verdict ?? "armed: measured bounce rate clears the bar",
            gate, dropAtr, s.Low(last), s.High(last), reversal, knifeRead, parameters, closed.Count);
    }

    private static ExhaustionDecision EvaluateShort(IReadOnlyList<IReadOnlyList<Candle>> history, List<Candle> closed,
        CausalSeries s, int last, double riseAtr, double brokenHigh, GateDecision gate, ExhaustionOptions o)
    {
        var trap = TrapDetectors.FailedBreakout(bins: o.Bins);
        trap.Learn(history);
        var trapRead = trap.Assess(s, last);
        if (trapRead is null)
            return Skip(ExhaustionSide.Short, "failed-breakout detector has no reading at this bar", gate, closed.Count, riseAtr, brokenHigh);
        if (trapRead.Rate.Samples < o.MinSamples)
            return Skip(ExhaustionSide.Short, $"bull-trap history too thin (n={trapRead.Rate.Samples} < {o.MinSamples})",
                gate, closed.Count, riseAtr, brokenHigh, trapRead);
        if (trapRead.Rate.Probability < o.TrapMinProbability)
            return Skip(ExhaustionSide.Short, $"learned bull-trap rate {trapRead.Rate.Probability:P0} < {o.TrapMinProbability:P0}",
                gate, closed.Count, riseAtr, brokenHigh, trapRead);

        var reversal = ReversalRate(history, long_: false, riseAtr, o);
        var verdict = Verdict(reversal, o, "fade");
        return new ExhaustionDecision(ExhaustionSide.Short, verdict is null, verdict ?? "armed: measured fade rate clears the bar",
            gate, riseAtr, brokenHigh, s.Low(last), reversal, trapRead, AdaptiveParameterDeriver.Derive(closed), closed.Count);
    }

    private static string? Verdict(OutcomeRate rate, ExhaustionOptions o, string what)
    {
        if (rate.Samples < o.MinSamples) return $"{what} history too thin (n={rate.Samples} < {o.MinSamples})";
        if (rate.Probability < o.MinReversalProbability)
            return $"measured {what} rate {rate.Probability:P0} < {o.MinReversalProbability:P0} (n={rate.Samples})";
        return null;
    }

    /// <summary>
    /// Counts, over the prior sessions, every bar that was the same kind of extreme (a new low after a
    /// drop for longs, a failed new high after a run for shorts), bucketed by how stretched it was,
    /// and whether it paid RewardAtr before costing RiskAtr within Horizon bars. A bar that touches both
    /// in one candle counts as a loss; an unresolved horizon at the end of a session is left out.
    /// </summary>
    public static OutcomeRate ReversalRate(IReadOnlyList<IReadOnlyList<Candle>> history, bool long_, double stretchAtr, ExhaustionOptions o)
    {
        var rows = new List<(double Stretch, bool Paid)>();
        foreach (var session in history)
        {
            if (session.Count <= Warmup + 1) continue;
            var s = new CausalSeries(session);
            for (int i = Warmup; i + 1 < session.Count; i++)
            {
                double stretch;
                if (long_) { if (!IsNewLow(s, i, o.MinMoveAtr, out stretch, o.StretchWindow)) continue; }
                else if (!IsFailedNewHigh(s, i, o.MinMoveAtr, out stretch, out _, o.StretchWindow)) continue;

                var paid = Paid(s, i, long_, o);
                if (paid is null) break;
                rows.Add((stretch, paid.Value));
            }
        }

        var binner = new QuantileBinner(rows.Select(r => r.Stretch), o.Bins);
        var table = new ConditionalOutcomeTable<int>();
        foreach (var (stretch, paid) in rows) table.Add(binner.Bin(stretch), paid);
        return table.Rate(binner.Bin(stretchAtr));
    }

    private static bool? Paid(CausalSeries s, int i, bool long_, ExhaustionOptions o)
    {
        if (i + o.Horizon >= s.Candles.Count) return null;
        double atr = Math.Max(s.Atr[i], 1e-9);
        double entry = s.Close(i);
        double win = long_ ? entry + o.RewardAtr * atr : entry - o.RewardAtr * atr;
        double lose = long_ ? entry - o.RiskAtr * atr : entry + o.RiskAtr * atr;
        for (int j = i + 1; j <= i + o.Horizon; j++)
        {
            bool hitWin = long_ ? s.High(j) >= win : s.Low(j) <= win;
            bool hitLose = long_ ? s.Low(j) <= lose : s.High(j) >= lose;
            if (hitLose) return false;
            if (hitWin) return true;
        }
        return false;
    }

    private static ExhaustionDecision Skip(ExhaustionSide side, string reason, GateDecision gate, int bars,
        double move = 0, double extreme = 0, TrapAssessment? trap = null) =>
        new(side, false, reason, gate, move, extreme, 0, null, trap, null, bars);
}
