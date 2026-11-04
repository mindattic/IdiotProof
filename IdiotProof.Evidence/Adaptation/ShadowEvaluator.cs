using IdiotProof.Models;

namespace IdiotProof.Evidence.Adaptation;

/// <summary>How one variant would have done today, so far.</summary>
public sealed record VariantScore(ParameterVariant Variant, int Wins, int Losses, double MeanReturnPercent,
    double PosteriorAlpha, double PosteriorBeta, double PosteriorMean, double ProbabilityBest);

public sealed record ShadowEvaluation(IReadOnlyList<VariantScore> Scores, int ChosenIndex, int ClosedBars)
{
    public ParameterVariant Chosen => Scores[ChosenIndex].Variant;
}

/// <summary>
/// Shadow-replays each parameter variant over today's bars that have already closed and lets a
/// Thompson bandit pick among them. The variant callback is handed ONLY the closed slice, so it
/// cannot see the future even by accident, and any trade that is still open at the end of that
/// slice is dropped as unresolved rather than marked to the last price. Every call rebuilds the
/// posteriors from scratch, so evaluating twice on the same bars gives the same answer.
/// </summary>
public static class ShadowEvaluator
{
    public static ShadowEvaluation Evaluate(
        IReadOnlyList<Candle> candles,
        DateTime nowUtc,
        IReadOnlyList<ParameterVariant> variants,
        Func<ParameterVariant, IReadOnlyList<Candle>, IEnumerable<ShadowTrade>> simulate,
        int seed,
        double priorAlpha = 1,
        double priorBeta = 1)
    {
        if (variants.Count == 0) throw new ArgumentException("At least one variant is required.", nameof(variants));
        var closed = InformationGate.ClosedBars(candles, nowUtc).AsReadOnly();
        var lastClosedEnd = closed.Count > 0 ? closed[^1].EndUtc : DateTime.MinValue;

        var bandit = new ThompsonBandit(variants.Count, seed, priorAlpha, priorBeta);
        var tallies = new (int Wins, int Losses, double SumRet, int N)[variants.Count];
        for (int v = 0; v < variants.Count; v++)
        {
            foreach (var t in simulate(variants[v], closed))
            {
                if (t.ExitUtc > lastClosedEnd || t.ExitUtc > nowUtc)
                    throw new InvalidOperationException($"Variant '{variants[v].Name}' reported an exit after the last closed bar: lookahead.");
                bandit.Update(v, t.Won);
                tallies[v] = (tallies[v].Wins + (t.Won ? 1 : 0), tallies[v].Losses + (t.Won ? 0 : 1),
                    tallies[v].SumRet + t.ReturnPercent, tallies[v].N + 1);
            }
        }

        var pBest = bandit.ProbabilityBest(2000);
        int chosen = bandit.Choose();
        var scores = new List<VariantScore>(variants.Count);
        for (int v = 0; v < variants.Count; v++)
        {
            var (a, b) = bandit.Posterior(v);
            scores.Add(new VariantScore(variants[v], tallies[v].Wins, tallies[v].Losses,
                tallies[v].N == 0 ? 0 : tallies[v].SumRet / tallies[v].N, a, b, bandit.PosteriorMean(v), pBest[v]));
        }
        return new ShadowEvaluation(scores, chosen, closed.Count);
    }

    /// <summary>
    /// A self-contained long bracket simulator: enter at the close of every bar where
    /// <paramref name="entrySignal"/> holds (one position at a time), exit on stop / trailing stop
    /// (stop-first, worst case) / target within later bars of the same slice. Trades still open when
    /// the slice ends are not reported.
    /// </summary>
    public static IEnumerable<ShadowTrade> SimulateLongBracket(ParameterVariant variant, IReadOnlyList<Candle> closed,
        Func<IReadOnlyList<Candle>, int, bool> entrySignal)
    {
        int i = 0;
        while (i < closed.Count)
        {
            if (!entrySignal(closed, i)) { i++; continue; }
            double entry = (double)closed[i].Close;
            if (entry <= 0) { i++; continue; }
            double stop = entry * (1 - variant.StopLossPercent / 100);
            double? target = variant.TakeProfitPercent is { } tp ? entry * (1 + tp / 100) : null;
            double peak = entry;
            int exitBar = -1;
            double exitPrice = 0;
            for (int j = i + 1; j < closed.Count; j++)
            {
                var bar = closed[j];
                double floor = stop;
                if (variant.TrailingStopPercent is { } tr) floor = Math.Max(floor, peak * (1 - tr / 100));
                if ((double)bar.Low <= floor) { exitBar = j; exitPrice = Math.Min(floor, (double)bar.Open); break; }
                if (target is { } t && (double)bar.High >= t) { exitBar = j; exitPrice = t; break; }
                peak = Math.Max(peak, (double)bar.High);
            }
            if (exitBar < 0) yield break;
            yield return new ShadowTrade(closed[i].EndUtc, closed[exitBar].EndUtc, (exitPrice / entry - 1) * 100);
            i = exitBar + 1;
        }
    }
}
