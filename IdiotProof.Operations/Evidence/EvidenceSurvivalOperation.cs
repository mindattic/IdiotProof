using IdiotProof.Core.Data;
using IdiotProof.Evidence.Survival;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceSurvivalArgs
{
    [Option("strategy", Description = "Only replay trades whose strategy/family name contains this text (case-insensitive).")]
    public string? Strategy { get; init; }

    [Option("symbol", Description = "Only this symbol's replay trades.")]
    public string? Symbol { get; init; }

    [Option("from", Description = "First trade day (yyyy-MM-dd ET).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last trade day (yyyy-MM-dd ET).")]
    public DateOnly? To { get; init; }

    [Option("top", Description = "Report at most this many strategies, most trades first (default 8).")]
    public int Top { get; init; } = 8;

    [Option("min-trades", Description = "Fewest trades a strategy needs to get its own curves (default 20).")]
    public int MinTrades { get; init; } = 20;

    [Option("checkpoints", Description = "Hold minutes at which to read the curves, comma-separated (default 5,15,30,60,120).")]
    public string Checkpoints { get; init; } = "5,15,30,60,120";
}

/// <summary>
/// How long replayed trades stay open before they hit target, stop or trail, measured with
/// survival analysis: Kaplan-Meier for the hold itself, Aalen-Johansen cumulative incidence for the
/// competing exits (sell-by and end of data censor a hold instead of counting as either), and Cox
/// proportional hazards for what makes the target come sooner. The suggested sell-by is where the
/// target's incidence stops rising: advisory only, it never edits a strategy.
/// </summary>
[Operation("evidence survival", Summary = "Survival analysis of exits: Kaplan-Meier hold curves, competing-risk incidence of target vs stop vs trail, Cox hazard ratios, and an advisory sell-by from replay trades.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence survival")]
[Example("idiotproof evidence survival --strategy gapper --checkpoints 10,30,60")]
public sealed class EvidenceSurvivalOperation : IOperation<EvidenceSurvivalArgs>
{
    /// <summary>Competing causes, in the order the incidence arrays use (1-based cause ids).</summary>
    public static readonly string[] Causes = { "target", "stop", "trail" };

    /// <summary>1 target, 2 stop, 3 trail (trailing stop or peak giveback), 0 censored (sell-by, While failed, end of data).</summary>
    public static int CauseOf(string? reason) => reason switch
    {
        "TargetHit" => 1,
        "StopLoss" => 2,
        "TrailingStop" or "PeakGiveback" => 3,
        _ => 0,
    };

    private static readonly string[] CovariateNames = { "dist-vwap %", "dist-window-high %", "log volume ratio", "log entry price" };

    public async Task<OperationResult> ExecuteAsync(EvidenceSurvivalArgs args, OperationContext ctx)
    {
        if (args.Top < 1) return OperationResult.Usage("--top must be >= 1.");
        if (args.MinTrades < 5) return OperationResult.Usage("--min-trades must be >= 5.");
        if (!TryParseCheckpoints(args.Checkpoints, out var checkpoints))
            return OperationResult.Usage("--checkpoints needs positive minutes, e.g. 5,15,30,60.");

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var query = db.ReplayTrades.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(args.Symbol))
        {
            var symbol = args.Symbol.Trim().ToUpperInvariant();
            query = query.Where(t => t.Symbol == symbol);
        }
        if (args.From is { } from) { var key = from.ToString("yyyy-MM-dd"); query = query.Where(t => string.Compare(t.DateEt, key) >= 0); }
        if (args.To is { } to) { var key = to.ToString("yyyy-MM-dd"); query = query.Where(t => string.Compare(t.DateEt, key) <= 0); }
        var trades = await query
            .Select(t => new { t.Strategy, t.HoldMin, t.Reason, t.DistVwapPct, t.DistWinHighPct, t.EntryVolx, t.EntryPx })
            .ToListAsync(ctx.CancellationToken);
        if (!string.IsNullOrWhiteSpace(args.Strategy))
            trades = trades.Where(t => t.Strategy.Contains(args.Strategy.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (trades.Count == 0)
            return OperationResult.Ok(new { Trades = 0, Groups = Array.Empty<object>() }, "No replay trades match. Run replays first (idiotproof replay run / replay all).");

        var groups = new List<object>();
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("strategy").Column("n", Align.Right).Column("target", Align.Right).Column("stop", Align.Right)
            .Column("trail", Align.Right).Column("cens", Align.Right).Column("median hold", Align.Right);
        foreach (var m in checkpoints) table.Column($"T/S @{m}m", Align.Right);
        table.Column("sell-by", Align.Right);

        var ranked = trades.GroupBy(t => t.Strategy).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var g in ranked.Where(g => g.Count() >= args.MinTrades).Take(args.Top))
        {
            var obs = g.Select(t => new SurvivalObservation(Math.Max(0, t.HoldMin), CauseOf(t.Reason))).ToList();
            var km = SurvivalAnalysis.KaplanMeier(obs);
            var cif = SurvivalAnalysis.CumulativeIncidence(obs, Causes.Length);
            double median = SurvivalAnalysis.MedianTime(km);
            double sellBy = SurvivalAnalysis.FlatteningTime(cif, 1);
            var counts = Enumerable.Range(0, Causes.Length + 1).Select(c => obs.Count(o => o.Cause == c)).ToArray();
            var reads = checkpoints.Select(m => new
            {
                Minutes = m,
                Target = Read(cif, m, 1),
                Stop = Read(cif, m, 2),
                Trail = Read(cif, m, 3),
                StillOpen = SurvivalAnalysis.At(km, m, s => s.Time, s => s.Survival, 1.0),
            }).ToList();

            var row = new List<string>
            {
                g.Key, obs.Count.ToString(), counts[1].ToString(), counts[2].ToString(), counts[3].ToString(), counts[0].ToString(),
                double.IsNaN(median) ? "—" : $"{median:0}m",
            };
            row.AddRange(reads.Select(r => $"{EvidenceSupport.Rate(r.Target)}/{EvidenceSupport.Rate(r.Stop)}"));
            row.Add(double.IsNaN(sellBy) ? "—" : $"{sellBy:0}m");
            table.Row(row.ToArray());

            groups.Add(new
            {
                Strategy = g.Key, Trades = obs.Count, Target = counts[1], Stop = counts[2], Trail = counts[3], Censored = counts[0],
                MedianHoldMinutes = R(median), SuggestedSellByMinutes = R(sellBy),
                Checkpoints = reads.Select(r => new { r.Minutes, Target = R(r.Target), Stop = R(r.Stop), Trail = R(r.Trail), StillOpen = R(r.StillOpen) }).ToList(),
            });
        }

        var notes = new List<string>();
        int small = ranked.Count(g => g.Count() < args.MinTrades);
        if (small > 0) notes.Add($"{small} strateg{(small == 1 ? "y has" : "ies have")} fewer than {args.MinTrades} trades and no curves of their own (they still count in the Cox fit).");

        var cox = new List<object>();
        string coxText = "";
        foreach (var cause in new[] { 1, 2 })
        {
            var rows = trades.Where(t => t.EntryPx > 0 && t.EntryVolx > 0).ToList();
            int events = rows.Count(t => CauseOf(t.Reason) == cause);
            if (events < 10 * CovariateNames.Length)
            {
                notes.Add($"Cox for {Causes[cause - 1]}: {events} event(s), need {10 * CovariateNames.Length} (10 per covariate); skipped.");
                continue;
            }
            var fit = CoxModel.Fit(
                rows.Select(t => (double)Math.Max(0, t.HoldMin)).ToList(),
                rows.Select(t => CauseOf(t.Reason) == cause).ToList(),
                rows.Select(t => new[] { t.DistVwapPct, t.DistWinHighPct, Math.Log(t.EntryVolx), Math.Log(t.EntryPx) }).ToList(),
                CovariateNames);
            cox.Add(new
            {
                Cause = Causes[cause - 1], fit.Observations, fit.Events, fit.Converged,
                Coefficients = fit.Coefficients.Select(c => new { c.Name, HazardRatio = R(c.HazardRatio), Low = R(c.Low), High = R(c.High), PValue = R(c.PValue) }).ToList(),
            });
            var ct = new TextTable { MaxWidth = ctx.Width }.Column("covariate").Column("HR", Align.Right).Column("95% CI", Align.Right).Column("p", Align.Right);
            foreach (var c in fit.Coefficients)
                ct.Row(c.Name, $"{c.HazardRatio:0.###}", $"{c.Low:0.###}–{c.High:0.###}", c.PValue < 0.001 ? "<0.001" : $"{c.PValue:0.000}");
            coxText += $"\nCox, time to {Causes[cause - 1]} (other exits censored), {fit.Events} events of {fit.Observations}{(fit.Converged ? "" : ", NOT converged")}:\n{ct.Render()}";
        }

        var human = $"Exit survival over {trades.Count} replay trade(s)\n{table.Render()}\n" +
            "T/S @m = cumulative incidence of target / stop by that hold minute (Aalen-Johansen; sell-by, While failed and end of data censor the hold). " +
            "Sell-by = the minute the target's incidence reaches 95% of its final level: advisory only, nothing is changed." +
            (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "") + coxText +
            (cox.Count > 0 ? $"\nHR > 1: that exit comes sooner as the covariate rises. {cox.Count * CovariateNames.Length} hazard ratios were tested, so expect about {cox.Count * CovariateNames.Length * 0.05:0.#} at p<0.05 by chance." : "");

        return OperationResult.Ok(new { Trades = trades.Count, Checkpoints = checkpoints, Groups = groups, Cox = cox, Notes = notes }, human);
    }

    private static double Read(IReadOnlyList<IncidenceStep> cif, double minutes, int cause) =>
        SurvivalAnalysis.At(cif, minutes, s => s.Time, s => s.Incidence[cause - 1], 0.0);

    internal static bool TryParseCheckpoints(string? text, out List<int> minutes)
    {
        minutes = new List<int>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var m) || m <= 0) return false;
            minutes.Add(m);
        }
        minutes = minutes.Distinct().OrderBy(m => m).ToList();
        return minutes.Count > 0;
    }

    private static double R(double v) => EvidenceSupport.Round(v);
}
