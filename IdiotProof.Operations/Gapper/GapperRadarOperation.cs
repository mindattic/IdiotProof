using System.Globalization;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperRadarArgs
{
    [Option("date", Description = "The session to scan for, yyyy-MM-dd (default: today before 16:00 ET, else the next session). A past session reads what was known by 20:00 ET the evening before.")]
    public DateOnly? Date { get; init; }

    [Option("top", Description = "How many names to list (default 20, max 300).", Short = 'n')]
    public int Top { get; init; } = 20;

    [Option("all", Description = "Also list names Alpaca will not trade (excluded by default).")]
    public bool All { get; init; }

    [Option("why", Description = "Show every reason, warning and headline under each name.")]
    public bool Why { get; init; }

    [Option("dir", Description = @"Data cache folder (default D:\IdiotProofData\radar).")]
    public string? Dir { get; init; }
}

[Operation("gapper radar", Summary = "Stocks that could gap up next session, ranked by the evening-before signs the gapper study measured (recent +100% day, top-10 mover today, dollar volume 5x, halts, reverse-split timing, price, catalyst type, Momentum. picks), each with its reasons and warnings; Alpaca-untradable names excluded. Read-only.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper radar")]
[Example("idiotproof gapper radar --top 10 --why")]
[Example("idiotproof gapper radar --date 2026-11-10")]
public sealed class GapperRadarOperation : IOperation<GapperRadarArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperRadarArgs args, OperationContext ctx)
    {
        if (args.Top is < 1 or > 300) return OperationResult.Usage("--top must be 1-300.");
        var owner = ctx.RequireOwner();
        var run = await ctx.Get<GapperRadarService>().RunAsync(owner, args.Date, args.Dir, ctx.CancellationToken);
        var shown = run.Rows.Where(r => args.All || !r.Score.Excluded).Take(args.Top).ToList();
        var excluded = run.Rows.Count(r => r.Score.Excluded);

        var data = new
        {
            Session = run.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Basis = run.Basis.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            run.AsOfUtc, run.Universe, run.Candidates, Excluded = excluded, run.Notes,
            Rows = shown.Select(r => new
            {
                r.Facts.Symbol, r.Score.Points, r.Score.Excluded, r.Facts.PriorClose, r.Facts.HighPctD1, r.Facts.ClosePctD1, r.Facts.RankHighD1,
                r.Facts.DollarVolumeRatioD1, r.Facts.Up100Last10, r.Facts.Up20Last5, r.Facts.DaysSinceReverseSplit, r.Facts.ReverseSplitOnD, r.Facts.ReverseSplitOnD1,
                r.Facts.HaltsLast5, r.Facts.Tradable, r.Facts.MomentumRank,
                Reasons = r.Score.Reasons.Select(x => new { x.Group, x.Sign, x.Lift, x.Points }),
                r.Score.Flags, r.Facts.CatalystHeadlines,
            }).ToList(),
        };
        if (run.Rows.Count == 0)
            return OperationResult.Ok(data, $"Gapper radar for {run.Session:yyyy-MM-dd}: no candidates. " + string.Join(" ", run.Notes));

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("sym").Column("pts", Align.Right).Column("price", Align.Right).Column("today hi/cl", Align.Right)
            .Column("$vol x", Align.Right).Column("reasons", maxWidth: 70).Column("warn");
        int i = 0;
        foreach (var r in shown)
        {
            var f = r.Facts;
            table.Row((++i).ToString(CultureInfo.InvariantCulture), f.Symbol + (r.Score.Excluded ? " (x)" : ""), r.Score.Points.ToString("0.0", CultureInfo.InvariantCulture),
                f.PriorClose is { } p ? p.ToString(p < 1 ? "0.0000" : "0.00", CultureInfo.InvariantCulture) : "-",
                $"{Pct(f.HighPctD1)}/{Pct(f.ClosePctD1)}",
                f.DollarVolumeRatioD1 is { } v ? v >= 100 ? ">100" : v.ToString("0.0", CultureInfo.InvariantCulture) : "-",
                string.Join("; ", r.Score.Reasons.Where(x => x.Points > 0 || x.Group == "Momentum.").Select(x => x.Sign)),
                r.Score.Flags.Count == 0 ? "" : r.Score.Flags.Count.ToString(CultureInfo.InvariantCulture));
        }
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Gapper radar for {run.Session:yyyy-MM-dd} (signs known by {Fmt.Et(run.AsOfUtc)}, basis session {run.Basis:yyyy-MM-dd}): " +
                        $"{run.Candidates} candidate(s) from {run.Universe} liquid names{(excluded > 0 && !args.All ? $", {excluded} Alpaca-untradable hidden (--all)" : "")}.");
        text.AppendLine(table.Render());
        if (args.Why)
            foreach (var r in shown)
            {
                text.AppendLine($"{r.Facts.Symbol}  {r.Score.Points:0.0} pts");
                foreach (var x in r.Score.Reasons) text.AppendLine($"   + {x.Sign}  (lift {x.Lift:0.#}x, {x.Points:+0.0;-0.0} pts)");
                foreach (var w in r.Score.Flags) text.AppendLine($"   ! {w}");
                foreach (var h in r.Facts.CatalystHeadlines) text.AppendLine($"   > {h}");
            }
        foreach (var n in run.Notes) text.AppendLine("Note: " + n);
        text.Append("Points are log2 of the gapper study's train lift for a next-day high of +50% or more, strongest sign per group; they rank names, they are not odds. " +
                    "Out of sample (43 sessions, 2026-09-10..11-09) 7.3% of the radar's top 20 ran +50% (36x the base rate) against 6.6% for a plain top-gainers screen: " +
                    "not significantly better at the top 20, better at the top 50 (20% of runners caught vs 15%). No long entry on these names has survived costs (IP-T494). Watch them; do not chase them.");
        return OperationResult.Ok(data, text.ToString());
    }

    private static string Pct(double? v) => v is { } x ? x.ToString("+0;-0", CultureInfo.InvariantCulture) + "%" : "-";
}
