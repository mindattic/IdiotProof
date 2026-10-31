using IdiotProof.Blazor.Data;
using IdiotProof.Monitor;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ReplayShowArgs
{
    [Argument(0, Name = "run", Description = "Run id or its first 6+ characters (from `replay list`).")]
    public string Run { get; init; } = "";

    [Option("bars", Description = "Which per-bar rows to print: marked (entries/exits, default), session (in-session bars) or all.")]
    public string Bars { get; init; } = "marked";
}

/// <summary>
/// One replay run from the SQL feature store: its round-trips (entry features → P&amp;L label, as
/// ReplayTrades) and its per-minute feature vectors (ReplayBars: OHLCV, VWAP, window high,
/// volume ratio, conditions passed, in-session, fire/exit flags).
/// </summary>
[Operation("replay show", Summary = "One replay run: its round-trips with entry features and its per-bar features (OHLCV, VWAP, volume ratio, conditions passed).", Risk = OperationRisk.Read)]
[Example("idiotproof replay show 0190a3f2")]
[Example("idiotproof replay show 0190a3f2 --bars session")]
public sealed class ReplayShowOperation : IOperation<ReplayShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayShowArgs args, OperationContext ctx)
    {
        var mode = (args.Bars ?? "marked").Trim().ToLowerInvariant();
        if (mode is not ("marked" or "session" or "all")) return OperationResult.Usage("--bars must be marked, session or all.");
        var run = await ReplayKit.FindRunAsync(ctx, args.Run);

        List<ReplayTrade> trades; List<ReplayBar> bars;
        await using (var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken))
        {
            trades = await db.ReplayTrades.AsNoTracking().Where(t => t.ReplayRunId == run.Id).OrderBy(t => t.EntryMin).ToListAsync(ctx.CancellationToken);
            bars = await db.ReplayBars.AsNoTracking().Where(b => b.ReplayRunId == run.Id).OrderBy(b => b.Min).ToListAsync(ctx.CancellationToken);
        }
        // Runs written before the feature store existed: flatten on the fly (read-only).
        if (bars.Count == 0 && !string.IsNullOrEmpty(run.DataJson)) (trades, bars) = ReplayFeatures.Extract(run);

        var kv = new KeyValueBlock()
            .Add("run", run.Id.ToString())
            .Add("replay", $"{run.Symbol} {run.DateEt} · \"{run.Strategy}\" · {run.Kind.ToUpperInvariant()} · {run.Feed}")
            .Add("generated", Fmt.Et(run.GeneratedUtc))
            .Add("result", run.Fired ? $"{run.PayoffCount} round-trip(s), first at {run.FirstFireEt} ET, total {Fmt.PercentPoints((decimal)run.TotalPnl, 2, signed: true)}" : "no fire")
            .Add("bars", $"{bars.Count} ({bars.Count(b => b.InSession)} in session)");

        var text = kv.Render();
        if (trades.Count > 0)
        {
            var tt = new TextTable { MaxWidth = ctx.Width }
                .Column("entry ET").Column("entry", Align.Right).Column("exit ET").Column("exit", Align.Right).Column("hold", Align.Right)
                .Column("P&L", Align.Right).Column("why").Column("vs VWAP", Align.Right).Column("vs high", Align.Right).Column("vol×", Align.Right);
            foreach (var t in trades)
                tt.Row(t.EntryEt, Fmt.Money(t.EntryPx), t.ExitEt, Fmt.Money(t.ExitPx), $"{t.HoldMin}m", Fmt.PercentPoints((decimal)t.PnlPct, 2, signed: true),
                    t.Reason, Fmt.PercentPoints((decimal)t.DistVwapPct, 2, signed: true), Fmt.PercentPoints((decimal)t.DistWinHighPct, 2, signed: true), t.EntryVolx.ToString("0.##"));
            text += "\n\nRound-trips:\n" + tt.Render();
        }

        var shown = mode switch
        {
            "all" => bars,
            "session" => bars.Where(b => b.InSession).ToList(),
            _ => bars.Where(b => b.Fire || b.Exit).ToList(),
        };
        if (shown.Count > 0)
        {
            var bt = new TextTable { MaxWidth = ctx.Width }
                .Column("ET").Column("open", Align.Right).Column("high", Align.Right).Column("low", Align.Right).Column("close", Align.Right)
                .Column("volume", Align.Right).Column("vwap", Align.Right).Column("vol×", Align.Right).Column("cond", Align.Right).Column("flag");
            foreach (var b in shown)
                bt.Row(b.Et, Fmt.Money(b.Open), Fmt.Money(b.High), Fmt.Money(b.Low), Fmt.Money(b.Close), b.Volume.ToString("N0"),
                    Fmt.Money(b.Vwap), b.Volx.ToString("0.##"), $"{b.CondPassed}/{b.CondTotal}",
                    (b.Fire ? "ENTRY " : "") + (b.Exit ? "EXIT" : "") + (b.InSession ? "" : " (out of session)"));
            text += $"\n\nBars ({mode}):\n" + bt.Render();
        }

        return OperationResult.Ok(new
        {
            Run = ReplayKit.RunData(run),
            Trades = trades.Select(t => new { t.EntryEt, t.EntryPx, t.ExitEt, t.ExitPx, t.HoldMin, t.PnlPct, t.Reason, t.Won, t.EntryVwap, t.EntryWindowHigh, t.EntryVolx, t.DistVwapPct, t.DistWinHighPct }).ToList(),
            Bars = shown.Select(b => new { b.Et, b.Min, b.Open, b.High, b.Low, b.Close, b.Volume, b.Vwap, b.WindowHigh, b.Volx, b.InSession, b.CondPassed, b.CondTotal, b.Fire, b.Exit }).ToList(),
        }, text);
    }
}
