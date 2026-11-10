using System.Globalization;
using IdiotProof.Core.Research.Gappers;

namespace IdiotProof.Operations.Gapper;

public sealed record GapperRadarBacktestArgs
{
    [Option("from", Description = "First session to replay, yyyy-MM-dd.")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last session to replay, yyyy-MM-dd.")]
    public DateOnly? To { get; init; }

    [Option("out", Description = @"JSON output (default D:\IdiotProofData\radar\backtest-<from>-<to>.json; tools/momentum/edge/radar_backtest.py scores it).")]
    public string? Out { get; init; }

    [Option("dir", Description = @"Data cache folder (default D:\IdiotProofData\radar).")]
    public string? Dir { get; init; }
}

/// <summary>
/// Replays the gapper radar point in time over past sessions (IP-T514; was the MomentumTracker tool's radar-backtest
/// step): each session's ranking from what was known by 20:00 ET the evening before, with every liquid name's actual
/// next-session high, for the out-of-sample scoring script.
/// </summary>
[Operation("gapper radar-backtest", Summary = "Replay the gapper radar over past sessions point in time and write the rankings with each session's actual highs as JSON for scoring. Places no orders.", Risk = OperationRisk.Write)]
[Example("idiotproof gapper radar-backtest --from 2026-09-10 --to 2026-11-09")]
public sealed class GapperRadarBacktestOperation : IOperation<GapperRadarBacktestArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperRadarBacktestArgs args, OperationContext ctx)
    {
        if (args.From is not { } from || args.To is not { } to) return OperationResult.Usage("Give --from and --to (yyyy-MM-dd).");
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");
        var owner = ctx.RequireOwner();
        var days = await ctx.Get<GapperRadarService>().BacktestAsync(owner, from, to, args.Dir, ctx.CancellationToken);
        var outPath = args.Out ?? Path.Combine(GapperRadarService.DefaultDir, $"backtest-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.json");
        var json = days.Select(x => new
        {
            Session = x.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Basis = x.Basis.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Liquid,
            Rows = x.Rows.Select(r => new
            {
                r.Facts.Symbol, r.Score.Points, r.Facts.RankHighD1, r.Facts.RankCloseD1, r.Facts.HighPctD1, r.Facts.ClosePctD1, r.Facts.DollarVolumeRatioD1,
                r.Facts.PriorClose, Groups = r.Score.Reasons.Where(g => g.Points != 0).Select(g => g.Group).ToList(), r.Score.Flags.Count,
            }),
            x.HighPct,
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, System.Text.Json.JsonSerializer.Serialize(json), ctx.CancellationToken);
        return OperationResult.Ok(new { sessions = days.Count, path = outPath }, $"radar backtest: {days.Count} session(s) -> {outPath}");
    }
}
