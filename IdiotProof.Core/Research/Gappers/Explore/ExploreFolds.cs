namespace IdiotProof.Core.Research.Gappers.Explore;

/// <summary>One rolling-origin fold inside train: fit on [FitFrom, FitTo], evaluate on [EvalFrom, EvalTo].</summary>
public sealed record ExploreFold(int Number, DateOnly FitFrom, DateOnly FitTo, DateOnly EvalFrom, DateOnly EvalTo)
{
    public bool InFit(DateOnly d) => d >= FitFrom && d <= FitTo;
    public bool InEval(DateOnly d) => d >= EvalFrom && d <= EvalTo;
    public string Label => $"fold {Number} (fit {FitFrom:yyyy-MM}..{FitTo:yyyy-MM}, eval {EvalFrom:yyyy-MM}..{EvalTo:yyyy-MM})";
}

/// <summary>
/// The exploration's folds (explore-plan.md): train is cut into an initial fit year and four
/// half-year evaluation windows; each fold fits on everything before its window, and the last
/// <see cref="EmbargoTradingDays"/> trading days before the window are dropped from the fit because
/// features read D-1..D-5.
/// </summary>
public static class ExploreFolds
{
    public const int EmbargoTradingDays = 5;

    /// <summary>The four folds over the given train days (any order; duplicates ignored).</summary>
    public static List<ExploreFold> Build(IEnumerable<DateOnly> trainDays, int initialFitMonths = 12, int evalMonths = 6, int folds = 4)
    {
        var days = trainDays.Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0) return [];
        var start = new DateOnly(days[0].Year, days[0].Month, 1);
        var result = new List<ExploreFold>();
        for (int k = 0; k < folds; k++)
        {
            var evalStart = start.AddMonths(initialFitMonths + k * evalMonths);
            var evalEnd = evalStart.AddMonths(evalMonths).AddDays(-1);
            var evalDays = days.Where(d => d >= evalStart && d <= evalEnd).ToList();
            var before = days.Where(d => d < evalStart).ToList();
            if (evalDays.Count == 0 || before.Count <= EmbargoTradingDays) break;
            var fitTo = before[^(EmbargoTradingDays + 1)];
            result.Add(new ExploreFold(k + 1, days[0], fitTo, evalDays[0], evalDays[^1]));
        }
        return result;
    }
}
