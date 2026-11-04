using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Liquidity;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidencePoolsArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. SNDL MULN.")]
    public string[] Symbols { get; init; } = [];

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 20 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("horizon", Description = "Bars after an approach that decide raid / break / reject (default 10).")]
    public int Horizon { get; init; } = 10;

    [Option("proximity", Description = "How close counts as reaching a pool, in ATRs of the bar before (default 0.25).")]
    public double Proximity { get; init; } = 0.25;

    [Option("min-samples", Description = "Hide rows with fewer approaches than this (default 1).")]
    public int MinSamples { get; init; } = 1;
}

/// <summary>Where stops cluster and what price did when it got there: counted raid, break and reject rates per pool kind.</summary>
[Operation("evidence pools", Summary = "Stop-pool raid rates: how often price raided each pool kind (round numbers, prior-day/premarket/opening-range levels, VWAP bands) and reversed vs broke through, per side and price tier.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence pools SNDL MULN")]
[Example("idiotproof evidence pools SNDL --from 2026-09-01 --to 2026-10-30 --min-samples 20")]
public sealed class EvidencePoolsOperation : IOperation<EvidencePoolsArgs>
{
    public async Task<OperationResult> ExecuteAsync(EvidencePoolsArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol.");
        if (args.Horizon < 1) return OperationResult.Usage("--horizon must be >= 1.");
        if (args.Proximity < 0) return OperationResult.Usage("--proximity must be >= 0.");
        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(-20);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var options = new PoolRaidOptions { Horizon = args.Horizon, ProximityAtr = args.Proximity };
        var bars = await ctx.Get<IHistoricalBarStore>().GetBarsForSymbolsAsync(symbols, from, to, BarTimeframe.Minute1, ct: ctx.CancellationToken);

        var table = new PoolRaidTable();
        var perKey = new Dictionary<PoolKey, int[]>();
        int sessions = 0;
        foreach (var s in symbols)
        {
            var series = bars.TryGetValue(s, out var raw) ? EvidenceSupport.Filter(raw, EvidenceTimeframe.Minute, EvidenceSession.All) : [];
            if (series.Count == 0) continue;
            sessions += series.Select(EvidenceSupport.DateEt).Distinct().Count();
            foreach (var t in PoolRaidDetector.Touches(new StopPoolSeries(series), options))
            {
                table.Add(t);
                if (!perKey.TryGetValue(t.Key, out var counts)) perKey[t.Key] = counts = new int[3];
                counts[(int)t.Outcome]++;
            }
        }

        var rows = new List<object>();
        var text = new TextTable { MaxWidth = ctx.Width }
            .Column("pool").Column("side").Column("tier").Column("approaches", Align.Right).Column("reject", Align.Right)
            .Column("raid+rev", Align.Right).Column("break", Align.Right).Column("fail rate (95% CI)", Align.Right)
            .Column("reverse|pierce", Align.Right).Column("raid depth", Align.Right);
        foreach (var (key, counts) in perKey.OrderBy(k => k.Key.Kind).ThenBy(k => k.Key.Side).ThenBy(k => k.Key.Tier))
        {
            int n = counts.Sum();
            if (n < args.MinSamples) continue;
            var fail = table.FailRate(key);
            var rev = table.ReverseRate(key);
            var depth = table.MedianRaidDepth(key);
            rows.Add(new
            {
                Pool = key.Kind.ToString(), Side = key.Side.ToString(), Tier = TierName(key.Tier), Approaches = n,
                Reject = counts[0], RaidAndReverse = counts[1], BreakThrough = counts[2],
                FailRate = EvidenceSupport.Round(fail.Probability), FailLow = EvidenceSupport.Round(fail.Low), FailHigh = EvidenceSupport.Round(fail.High),
                Pierces = rev.Samples, ReverseRate = EvidenceSupport.Round(rev.Probability), MedianRaidDepth = depth,
            });
            text.Row(key.Kind.ToString(), key.Side.ToString().ToLowerInvariant(), TierName(key.Tier), n.ToString(),
                counts[0].ToString(), counts[1].ToString(), counts[2].ToString(),
                $"{EvidenceSupport.Rate(fail.Probability)} ({EvidenceSupport.Rate(fail.Low)}–{EvidenceSupport.Rate(fail.High)})",
                rev.Samples == 0 ? "—" : $"{EvidenceSupport.Rate(rev.Probability)} of {rev.Samples}",
                depth is { } d ? EvidenceSupport.Rate(d) : "—");
        }

        var summary = rows.Count == 0
            ? "No pool approaches in that range."
            : text.Render() + $"\n{table.Approaches} approaches over {sessions} symbol-sessions; horizon {args.Horizon} bars, reach {args.Proximity} ATR. " +
              "fail = rejected or pierced-then-reversed; rates are Laplace-smoothed with Wilson intervals.";
        return OperationResult.Ok(new { From = from, To = to, args.Horizon, args.Proximity, Approaches = table.Approaches, Pools = rows }, summary);
    }

    private static string TierName(int tier) => tier switch { 0 => "<$1", 1 => "$1-5", 2 => "$5-20", _ => "$20+" };
}
