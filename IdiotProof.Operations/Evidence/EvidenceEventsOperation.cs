using IdiotProof.Core.Data;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.EventStudy;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceEventsArgs
{
    [Option("from", Description = "First claim article day (yyyy-MM-dd, default: 180 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last claim article day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("type", Description = "Only this claim type (Earnings, Contract, Insider, MA, Guidance, Regulatory, News, IndexEvent).")]
    public string? Type { get; init; }

    [Option("windows", Description = "Event windows in trading days, start:end, comma-separated (default -1:1,0:1,0:5).")]
    public string Windows { get; init; } = "-1:1,0:1,0:5";

    [Option("estimation", Description = "Estimation-window length in trading days (default 120).")]
    public int Estimation { get; init; } = 120;

    [Option("gap", Description = "Trading days between the estimation window and the event window (default 10).")]
    public int Gap { get; init; } = 10;

    [Option("min-events", Description = "Fewest valid events a group needs to be reported (default 5).")]
    public int MinEvents { get; init; } = 5;

    [Option("bootstrap", Description = "Placebo bootstrap draws (default 1000; 0 skips it).")]
    public int Bootstrap { get; init; } = 1000;

    [Option("seed", Description = "Bootstrap seed (default 1337).")]
    public int Seed { get; init; } = 1337;

    [Option("benchmark", Description = "Market benchmark symbol (default SPY).")]
    public string Benchmark { get; init; } = "SPY";

    [Option("max-daily-move", Description = "Drop events whose windows hold a daily return above this (default 2.0 = +200%, or below -66.7%). Bars are split-adjusted from Alpaca corporate actions; this guard catches splits the feed missed.")]
    public double MaxDailyMove { get; init; } = 2.0;
}

/// <summary>
/// Did the claims the Research scanner found actually move prices? A market-model event study of every
/// single-ticker claim: abnormal returns around the article day, tested across events with Patell's Z,
/// Boehmer-Musumeci-Poulsen's t and a placebo bootstrap, per claim type, type × sentiment, and insider
/// transaction code. "Signed" results flip bearish claims so a positive number means the sentiment was right.
/// </summary>
[Operation("evidence events", Summary = "Event study of Research claims: market-model abnormal returns and CARs per claim type/sentiment/insider code, with Patell, BMP and placebo-bootstrap significance.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence events")]
[Example("idiotproof evidence events --type Insider --windows 0:1,0:5 --from 2026-05-01")]
public sealed class EvidenceEventsOperation : IOperation<EvidenceEventsArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidenceEventsArgs args, OperationContext ctx)
    {
        if (!TryParseWindows(args.Windows, out var windows, out var error)) return OperationResult.Usage(error);
        if (args.Estimation < 20) return OperationResult.Usage("--estimation must be >= 20.");
        if (args.Gap < 0) return OperationResult.Usage("--gap must be >= 0.");
        if (args.MinEvents < 2) return OperationResult.Usage("--min-events must be >= 2.");
        if (args.Bootstrap < 0) return OperationResult.Usage("--bootstrap must be >= 0.");
        if (args.MaxDailyMove <= 0) return OperationResult.Usage("--max-daily-move must be > 0.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(-180);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");
        var benchmark = args.Benchmark.Trim().ToUpperInvariant();

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var query = db.ResearchClaims.AsNoTracking()
            .Where(c => !c.IsMacro && c.Ticker != "" && c.ArticleDate >= from && c.ArticleDate <= to);
        if (!string.IsNullOrWhiteSpace(args.Type)) query = query.Where(c => c.ClaimType == args.Type);
        var claims = await query.Select(c => new { c.Id, c.Ticker, c.ArticleDate, c.ClaimType, c.Sentiment }).ToListAsync(ctx.CancellationToken);
        var claimIds = claims.Where(c => c.ClaimType == "Insider").Select(c => c.Id).ToList();
        var codes = claimIds.Count == 0 ? new Dictionary<Guid, string>() : (await db.InsiderTransactions.AsNoTracking()
                .Where(t => claimIds.Contains(t.ClaimId)).Select(t => new { t.ClaimId, t.TransactionCode, t.DollarValue }).ToListAsync(ctx.CancellationToken))
            .GroupBy(t => t.ClaimId)
            .ToDictionary(g => g.Key, g => g.GroupBy(t => t.TransactionCode).OrderByDescending(x => x.Sum(t => Math.Abs(t.DollarValue ?? 0))).First().Key);

        if (claims.Count == 0)
            return OperationResult.Ok(new { From = from, To = to, Claims = 0, Groups = Array.Empty<object>() }, "No single-ticker claims in that range.");

        int lead = args.Estimation + args.Gap + windows.Max(w => Math.Max(0, -w.Start)) + 5;
        var barsFrom = from.AddDays(-(int)Math.Ceiling(lead * 1.5) - 10);
        var barsTo = to.AddDays(windows.Max(w => w.End) * 2 + 10);
        var today = EvidenceSupport.TodayEt(ctx);
        if (barsTo > today) barsTo = today;

        var symbols = claims.Select(c => c.Ticker.Trim().ToUpperInvariant()).Append(benchmark).Distinct().ToList();
        var adjusted = await ctx.Get<IHistoricalBarStore>().GetSplitAdjustedBarsForSymbolsAsync(symbols, barsFrom, barsTo, BarTimeframe.Day1, ct: ctx.CancellationToken);
        var bars = adjusted.Bars;
        if (!bars.TryGetValue(benchmark, out var benchBars) || benchBars.Count == 0)
            return OperationResult.Fail($"No daily bars for the benchmark {benchmark}.");

        var calendar = benchBars.Select(EvidenceSupport.DateEt).Distinct().OrderBy(d => d).ToList();
        var market = ReturnSeries.FromCloses(Closes(benchBars), calendar);
        var stocks = new Dictionary<string, ReturnSeries>(StringComparer.Ordinal);
        foreach (var (sym, list) in bars)
            if (sym != benchmark && list.Count > 0) stocks[sym] = ReturnSeries.FromCloses(Closes(list), calendar);

        var engine = new EventStudyEngine(calendar, market, stocks,
            new EventStudyOptions
            {
                EstimationLength = args.Estimation, Gap = args.Gap, MinEstimationObservations = Math.Max(10, args.Estimation / 2), MaxDailyMove = args.MaxDailyMove,
            });

        var specs = claims.Select(c =>
        {
            int sign = c.Sentiment switch { "Bullish" => 1, "Bearish" => -1, _ => 0 };
            return (c.Id, c.ClaimType, c.Sentiment, Spec: new EventSpec(c.Ticker.Trim().ToUpperInvariant(), c.ArticleDate, c.ClaimType, sign));
        }).ToList();

        var groups = new List<EventGroupResult>();
        var skipped = new Dictionary<string, int>();
        foreach (var window in windows)
        {
            var computed = specs.Select(s => (s.Id, s.ClaimType, s.Sentiment, Result: engine.Compute(s.Spec, window))).ToList();
            if (window == windows[0])
                foreach (var c in computed.Where(c => !c.Result.Valid))
                    skipped[c.Result.Skipped!] = skipped.GetValueOrDefault(c.Result.Skipped!) + 1;

            var keyed = new List<(string Group, EventResult Result)>();
            foreach (var c in computed)
            {
                keyed.Add((c.ClaimType, c.Result));
                keyed.Add(($"{c.ClaimType}/{c.Sentiment}", c.Result));
                if (codes.TryGetValue(c.Id, out var code)) keyed.Add(($"Insider/code {code}", c.Result));
            }
            foreach (var g in keyed.GroupBy(k => k.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var results = g.Select(x => x.Result).ToList();
                if (results.Count(r => r.Valid) < args.MinEvents) continue;
                groups.Add(engine.Aggregate(g.Key, results, window, args.Bootstrap, args.Seed));
            }
        }

        int valid = specs.Count - skipped.Values.Sum();
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("group").Column("window").Column("n", Align.Right).Column("mean CAR", Align.Right).Column("median", Align.Right)
            .Column("%>0", Align.Right).Column("Patell p", Align.Right).Column("BMP p", Align.Right).Column("placebo p", Align.Right)
            .Column("signed CAR", Align.Right).Column("signed p", Align.Right);
        foreach (var g in groups)
            table.Row(g.Group, g.Window.ToString(), g.Events.ToString(), EvidenceSupport.Pct(g.MeanCar), EvidenceSupport.Pct(g.MedianCar),
                EvidenceSupport.Rate(g.FractionPositive), P(g.PatellP), P(g.BmpP), P(g.BootstrapP),
                g.SignedEvents == 0 ? "—" : EvidenceSupport.Pct(g.MeanSignedCar), g.SignedEvents < 2 ? "—" : P(g.SignedBmpP));

        var skippedText = skipped.Count == 0 ? "none" : string.Join(", ", skipped.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}"));
        var splitText = adjusted.SplitDataAvailable
            ? $"{adjusted.TotalSplits} split(s) across {adjusted.SplitsApplied.Count} symbol(s) back-adjusted"
            : "no split data (bars are raw; only the daily-move guard catches splits)";
        var footer = $"{claims.Count} claims, {valid} with a full estimation + event window; skipped: {skippedText}; {splitText}.\n" +
                     $"Market model vs {benchmark}, estimation {args.Estimation} trading days ending {args.Gap} before the window; day 0 = first trading day on/after the article date. " +
                     "BMP is robust to event-induced variance; placebo p compares the mean CAR with random non-event days of the same symbols. " +
                     $"Signed CAR flips bearish claims (positive = the sentiment was right). Groups with fewer than {args.MinEvents} events are omitted; " +
                     $"{groups.Count} group-window tests were run, so expect about {groups.Count * 0.05:0.#} at p<0.05 by chance.";

        return OperationResult.Ok(new
        {
            From = from, To = to, Benchmark = benchmark, Claims = claims.Count, ValidEvents = valid, Skipped = skipped,
            SplitDataAvailable = adjusted.SplitDataAvailable, SplitsApplied = adjusted.TotalSplits,
            Windows = windows.Select(w => w.ToString()).ToList(), Tests = groups.Count,
            Groups = groups.Select(g => new
            {
                g.Group, Window = g.Window.ToString(), g.Events, MeanCar = R(g.MeanCar), MedianCar = R(g.MedianCar), FractionPositive = R(g.FractionPositive),
                CrossSectionalT = R(g.CrossSectionalT), CrossSectionalP = R(g.CrossSectionalP), PatellZ = R(g.PatellZ), PatellP = R(g.PatellP),
                BmpT = R(g.BmpT), BmpP = R(g.BmpP), BootstrapP = R(g.BootstrapP),
                g.SignedEvents, MeanSignedCar = R(g.MeanSignedCar), SignedBmpT = R(g.SignedBmpT), SignedBmpP = R(g.SignedBmpP),
            }).ToList(),
        }, $"{table.Render()}\n{footer}");
    }

    internal static bool TryParseWindows(string? text, out List<EventWindow> windows, out string error)
    {
        windows = new List<EventWindow>();
        error = "";
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split(':');
            if (ends.Length != 2 || !int.TryParse(ends[0], out var a) || !int.TryParse(ends[1], out var b) || b < a || a < -20 || b > 60)
            {
                error = $"Bad window '{part}': use start:end trading days, e.g. -1:1 (start <= end, within -20..60).";
                return false;
            }
            windows.Add(new EventWindow(a, b));
        }
        if (windows.Count == 0) { error = "--windows needs at least one start:end window."; return false; }
        windows = windows.Distinct().ToList();
        return true;
    }

    private static Dictionary<DateOnly, double> Closes(IEnumerable<IdiotProof.Models.Candle> bars)
    {
        var d = new Dictionary<DateOnly, double>();
        foreach (var c in bars.Where(c => c.IsValid && c.Close > 0).OrderBy(c => c.StartUtc)) d[EvidenceSupport.DateEt(c)] = (double)c.Close;
        return d;
    }

    private static string P(double p) => double.IsNaN(p) ? "—" : p < 0.001 ? "<0.001" : p.ToString("0.000");
    private static double R(double v) => EvidenceSupport.Round(v);
}
