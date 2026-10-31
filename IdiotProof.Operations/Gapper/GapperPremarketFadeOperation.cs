using System.Globalization;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.DataFeeds;
using IdiotProof.Models;
using IdiotProof.Monitor;
using IdiotProof.Operations.Market;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Gapper;

public enum PremarketFadeAction { Status, Run }

public sealed record GapperPremarketFadeArgs
{
    [Argument(0, Name = "action", Required = false, Description = "status (default): today's fade alerts; run: scan the movers for the pattern now.")]
    public PremarketFadeAction Action { get; init; } = PremarketFadeAction.Status;

    [Option("top", Description = "Movers to screen when running (default 50, max 50).")]
    public int Top { get; init; } = 50;
}

/// <summary>
/// The premarket blow-off/fade detector. <c>run</c> screens Alpaca's live movers and walks each
/// one's 4:00 AM ET-to-now minute bars through <see cref="PremarketFadeScanner.ComputeFade"/> —
/// the detector the Monitor's 9:00-10:00 AM ET pass uses — and lists every name that surged 20%+
/// off its premarket low (ESPECIALLY when it already gave back 10%+ of the run). Detection only:
/// no strategy, no order, and no phone alert (the Monitor's own pass sends those).
/// <c>status</c> lists the alerts the Monitor raised today.
/// </summary>
[Operation("gapper premarket-fade", Summary = "Premarket blow-off/fade detector: `run` scans the movers now (detection only), `status` lists today's fade alerts.", Risk = OperationRisk.Read)]
[Example("idiotproof gapper premarket-fade")]
[Example("idiotproof gapper premarket-fade run")]
public sealed class GapperPremarketFadeOperation : IOperation<GapperPremarketFadeArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperPremarketFadeArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var todayEt = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        return args.Action == PremarketFadeAction.Run ? await RunAsync(args, ctx, todayEt) : await StatusAsync(ctx, owner, todayEt);
    }

    private static async Task<OperationResult> StatusAsync(OperationContext ctx, Guid owner, DateOnly todayEt)
    {
        var dayStartUtc = GapperBacktestDayOperation.EtToUtc(todayEt, TimeOnly.MinValue);
        var alerts = (await ctx.Get<AuditLogRepository>().GetByCategoryAsync("premarket-fade-alert", 200, ctx.CancellationToken))
            .Where(a => a.TimestampUtc >= dayStartUtc && (a.UserId is null || a.UserId == owner))
            .OrderBy(a => a.TimestampUtc).ToList();
        var data = alerts.Select(a => new { a.TimestampUtc, a.Message }).ToList();
        if (alerts.Count == 0)
            return OperationResult.Ok(data, $"No premarket fade alerts today ({todayEt:yyyy-MM-dd}). The Monitor scans 9:00-10:00 AM ET; `gapper premarket-fade run` scans now.");
        var table = new TextTable { MaxWidth = ctx.Width }.Column("at").Column("alert");
        foreach (var a in alerts) table.Row(Fmt.EtTime(a.TimestampUtc), a.Message);
        return OperationResult.Ok(data, table.Render() + $"\n{alerts.Count} alert(s) today.");
    }

    private static async Task<OperationResult> RunAsync(GapperPremarketFadeArgs args, OperationContext ctx, DateOnly todayEt)
    {
        if (args.Top is < 1 or > 50) return OperationResult.Usage("--top must be 1-50.");
        var feed = ctx.Get<IMarketDataFeed>();
        if (feed is MockDataFeed)
            return OperationResult.Blocked("The Hub is on the Mock data feed (no Alpaca keys resolved); a fade scan of made-up prices would be noise.");

        List<string> symbols;
        await using (var api = await MarketData.OpenAsync(ctx))
        {
            var root = await api.GetDataAsync("/v1beta1/screener/stocks/movers",
                MarketData.Q(("top", args.Top.ToString(CultureInfo.InvariantCulture))), ctx.CancellationToken);
            symbols = MarketData.Obj(root, "gainers") is { ValueKind: JsonValueKind.Array } g
                ? g.EnumerateArray().Select(m => MarketData.Str(m, "symbol")).OfType<string>()
                    .Select(s => s.ToUpperInvariant()).Distinct().ToList()
                : [];
        }

        var startUtc = GapperBacktestDayOperation.EtToUtc(todayEt, new TimeOnly(4, 0));
        var flagged = new List<PremarketFadeScanner.FadeCandidate>();
        var failed = new List<string>();
        foreach (var sym in symbols)
        {
            try
            {
                var bars = new List<Candle>();
                await foreach (var c in feed.GetHistoricalCandlesAsync(sym, startUtc, ctx.UtcNow, TimeSpan.FromMinutes(1), ctx.CancellationToken))
                    bars.Add(c);
                if (PremarketFadeScanner.ComputeFade(sym, bars) is { } hit) flagged.Add(hit);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { failed.Add(sym); }
        }

        flagged = flagged.OrderByDescending(f => f.Escalated).ThenByDescending(f => f.SurgePercent).ToList();
        var data = new { Screened = symbols.Count, Flagged = flagged, Failed = failed };
        var head = $"Premarket fade scan: {symbols.Count} movers screened since 4:00 AM ET, {flagged.Count} flagged (detection only — nothing was alerted, armed or ordered).";
        if (flagged.Count == 0) return OperationResult.Ok(data, head);
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("sym").Column("low", Align.Right).Column("peak", Align.Right).Column("now", Align.Right)
            .Column("surge", Align.Right).Column("giveback", Align.Right).Column("verdict");
        foreach (var f in flagged)
            table.Row(f.Symbol, Fmt.Money(f.PremarketLow), Fmt.Money(f.PeakPrice), Fmt.Money(f.CurrentPrice),
                Fmt.PercentPoints((decimal)f.SurgePercent, 1, signed: true), Fmt.PercentPoints((decimal)f.GivebackPercent, 1),
                f.Escalated ? "ESPECIALLY — fading off the peak" : "surging — don't chase the top");
        return OperationResult.Ok(data, head + "\n" + table.Render() + (failed.Count > 0 ? $"\nNo bars for: {string.Join(", ", failed)}" : ""));
    }
}
