using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdiotProof.Brokers;
using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>Paging for the multi-symbol options data endpoints, which key their arrays by symbol.</summary>
internal static class OptionsSeries
{
    /// <summary>Follows <c>next_page_token</c> and returns up to <paramref name="max"/> items of <c>{property}.{occ}</c>.</summary>
    public static async Task<List<JsonElement>> ReadAsync(AlpacaApi api, string path, string property, string occ,
        Dictionary<string, string?> query, int max, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        var q = new Dictionary<string, string?>(query) { ["symbols"] = occ, ["limit"] = Math.Min(max, 10_000).ToString(CultureInfo.InvariantCulture) };
        for (var page = 0; page < 50 && items.Count < max; page++)
        {
            var root = await api.GetDataAsync(path, q, ct);
            if (Obj(root, property) is { } bySymbol && bySymbol.TryGetProperty(occ, out var arr) && arr.ValueKind == JsonValueKind.Array)
                items.AddRange(arr.EnumerateArray());
            var token = NextPageToken(root);
            if (token is null) break;
            q["page_token"] = token;
        }
        return items.Count > max ? items[..max] : items;
    }

    /// <summary><c>{property}.{occ}</c> of a latest-* endpoint, or null.</summary>
    public static async Task<JsonElement?> LatestAsync(AlpacaApi api, string path, string property, string occ, OptionsFeed feed, CancellationToken ct)
    {
        var root = await api.GetDataAsync(path, Query(("symbols", occ), ("feed", FeedName(feed))), ct);
        return Obj(root, property) is { } bySymbol && Obj(bySymbol, occ) is { } item ? item : null;
    }

    /// <summary>The start of a window as RFC 3339 (midnight ET of the date) for the data API.</summary>
    public static string StartOf(DateOnly etDate) => EtMidnightUtc(etDate).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>The end of a window: midnight ET after the date.</summary>
    public static string EndOf(DateOnly etDate) => EtMidnightUtc(etDate.AddDays(1)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTime EtMidnightUtc(DateOnly etDate)
    {
        var local = etDate.ToDateTime(TimeOnly.MinValue);
        var offset = EasternClock.ToEasternOffset(DateTime.SpecifyKind(local.AddHours(5), DateTimeKind.Utc)).Offset;
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }
}

public sealed record OptionsBarsArgs
{
    [Argument(0, Name = "occ", Description = "OCC option symbol, e.g. AAPL261120C00250000.")]
    public string Occ { get; init; } = "";

    [Option("timeframe", Description = "Bar size: [1-59]Min, [1-23]Hour, 1Day, 1Week or [1,2,3,4,6,12]Month (default 1Day).")]
    public string Timeframe { get; init; } = "1Day";

    [Option("start", Description = "First ET date (yyyy-MM-dd); default 30 days back for daily bars, 5 for intraday.")]
    public DateOnly? Start { get; init; }

    [Option("end", Description = "Last ET date (yyyy-MM-dd); default today.")]
    public DateOnly? End { get; init; }

    [Option("limit", Description = "Most bars to return, oldest first (default 200).")]
    public int Limit { get; init; } = 200;
}

/// <summary>Historical OHLCV bars for one contract (<c>GET /v1beta1/options/bars</c>).</summary>
[Operation("options bars", Summary = "Historical OHLCV bars for one option contract.", Risk = OperationRisk.Read)]
[Example("idiotproof options bars AAPL261120C00250000")]
[Example("idiotproof options bars AAPL261120C00250000 --timeframe 15Min --start 2026-10-28")]
public sealed partial class OptionsBarsOperation : IOperation<OptionsBarsArgs>
{
    [GeneratedRegex("^([1-9][0-9]?)(Min|T|Hour|H|Day|D|Week|W|Month|M)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeframeRegex();

    public async Task<OperationResult> ExecuteAsync(OptionsBarsArgs args, OperationContext ctx)
    {
        var contract = RequireOcc(args.Occ);
        var tf = args.Timeframe.Trim();
        var match = TimeframeRegex().Match(tf);
        if (!match.Success) return OperationResult.Usage($"--timeframe '{tf}' is not a bar size; use e.g. 1Min, 15Min, 1Hour, 1Day, 1Week, 1Month.");
        if (args.Limit is < 1 or > 10_000) return OperationResult.Usage("--limit must be 1-10000.");
        var intraday = match.Groups[2].Value.ToUpperInvariant() is "MIN" or "T" or "HOUR" or "H";

        var today = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var end = args.End ?? today;
        var start = args.Start ?? end.AddDays(intraday ? -5 : -30);
        if (start > end) return OperationResult.Usage("--start is after --end.");

        await using var api = await DataApiAsync(ctx);
        var query = Query(("timeframe", tf), ("start", OptionsSeries.StartOf(start)), ("end", OptionsSeries.EndOf(end)), ("sort", "asc"));
        var bars = (await OptionsSeries.ReadAsync(api, "/v1beta1/options/bars", "bars", contract.OccSymbol, query, args.Limit, ctx.CancellationToken))
            .Select(b => new OptionBar(Time(b, "t") ?? DateTime.MinValue, Dec(b, "o"), Dec(b, "h"), Dec(b, "l"), Dec(b, "c"), Dec(b, "v"), Dec(b, "n"), Dec(b, "vw")))
            .ToList();
        if (bars.Count == 0)
            return OperationResult.Ok(new { occ = contract.OccSymbol, timeframe = tf, bars }, $"No {tf} bars for {contract.OccSymbol} from {Date(start)} to {Date(end)}.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column(intraday ? "Time (ET)" : "Date").Column("Open", Align.Right).Column("High", Align.Right).Column("Low", Align.Right)
            .Column("Close", Align.Right).Column("Volume", Align.Right).Column("Trades", Align.Right).Column("VWAP", Align.Right);
        foreach (var b in bars)
            table.Row(intraday ? Fmt.Et(b.TimeUtc) : Fmt.EtDate(b.TimeUtc), Money(b.Open), Money(b.High), Money(b.Low), Money(b.Close),
                b.Volume is { } v ? Fmt.Number(v) : "-", b.Trades is { } n ? Fmt.Number(n) : "-", Money(b.Vwap));
        var head = $"{contract.OccSymbol}  ({contract.DisplayName})  {tf} bars, {Date(start)} to {Date(end)}";
        return OperationResult.Ok(new { occ = contract.OccSymbol, timeframe = tf, start = Date(start), end = Date(end), bars }, head + "\n" + table.Render());
    }
}

/// <summary>One options bar; time is the bar's UTC start.</summary>
public sealed record OptionBar(DateTime TimeUtc, decimal? Open, decimal? High, decimal? Low, decimal? Close, decimal? Volume, decimal? Trades, decimal? Vwap);

public sealed record OptionsTradesArgs
{
    [Argument(0, Name = "occ", Description = "OCC option symbol, e.g. AAPL261120C00250000.")]
    public string Occ { get; init; } = "";

    [Option("start", Description = "First ET date (yyyy-MM-dd); default 7 days back.")]
    public DateOnly? Start { get; init; }

    [Option("end", Description = "Last ET date (yyyy-MM-dd); default today.")]
    public DateOnly? End { get; init; }

    [Option("limit", Description = "Most trades to return, newest first (default 50).")]
    public int Limit { get; init; } = 50;
}

/// <summary>One options print.</summary>
public sealed record OptionTrade(DateTime TimeUtc, decimal? Price, decimal? Size, string? Exchange, string? Condition);

/// <summary>Historical trades (prints) for one contract (<c>GET /v1beta1/options/trades</c>), newest first.</summary>
[Operation("options trades", Summary = "Historical trades (prints) for one option contract, newest first.", Risk = OperationRisk.Read)]
[Example("idiotproof options trades AAPL261120C00250000")]
[Example("idiotproof options trades AAPL261120C00250000 --start 2026-10-29 --limit 200")]
public sealed class OptionsTradesOperation : IOperation<OptionsTradesArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsTradesArgs args, OperationContext ctx)
    {
        var contract = RequireOcc(args.Occ);
        if (args.Limit is < 1 or > 10_000) return OperationResult.Usage("--limit must be 1-10000.");
        var today = DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        var end = args.End ?? today;
        var start = args.Start ?? end.AddDays(-7);
        if (start > end) return OperationResult.Usage("--start is after --end.");

        await using var api = await DataApiAsync(ctx);
        var query = Query(("start", OptionsSeries.StartOf(start)), ("end", OptionsSeries.EndOf(end)), ("sort", "desc"));
        var trades = (await OptionsSeries.ReadAsync(api, "/v1beta1/options/trades", "trades", contract.OccSymbol, query, args.Limit, ctx.CancellationToken))
            .Select(t => new OptionTrade(Time(t, "t") ?? DateTime.MinValue, Dec(t, "p"), Dec(t, "s"), Str(t, "x"), Str(t, "c")))
            .ToList();
        if (trades.Count == 0)
            return OperationResult.Ok(new { occ = contract.OccSymbol, trades }, $"No trades in {contract.OccSymbol} from {Date(start)} to {Date(end)}.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time (ET)").Column("Price", Align.Right).Column("Size", Align.Right).Column("Exch").Column("Cond");
        foreach (var t in trades)
            table.Row(Fmt.Et(t.TimeUtc), Money(t.Price), t.Size is { } s ? Fmt.Number(s) : "-", t.Exchange ?? "-", t.Condition ?? "-");
        var volume = trades.Sum(t => t.Size ?? 0);
        var head = $"{contract.OccSymbol}  ({contract.DisplayName})  {trades.Count} trades, {Fmt.Number(volume)} contracts";
        return OperationResult.Ok(new { occ = contract.OccSymbol, start = Date(start), end = Date(end), trades }, head + "\n" + table.Render());
    }
}

/// <summary>The latest quote and latest trade for one contract (<c>/v1beta1/options/quotes/latest</c> and <c>/trades/latest</c>).</summary>
[Operation("options latest", Summary = "Latest NBBO quote and latest trade for one option contract, with exchanges and sizes.", Risk = OperationRisk.Read)]
[Example("idiotproof options latest AAPL261120C00250000")]
public sealed class OptionsLatestOperation : IOperation<OptionsOccArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsOccArgs args, OperationContext ctx)
    {
        var contract = RequireOcc(args.Occ);
        var occ = contract.OccSymbol;
        var ct = ctx.CancellationToken;
        await using var api = await DataApiAsync(ctx);
        var quote = await OptionsSeries.LatestAsync(api, "/v1beta1/options/quotes/latest", "quotes", occ, args.Feed, ct);
        var trade = await OptionsSeries.LatestAsync(api, "/v1beta1/options/trades/latest", "trades", occ, args.Feed, ct);
        if (quote is null && trade is null) return OperationResult.Fail($"Alpaca has no {FeedName(args.Feed)} quote or trade for {occ}.");

        var block = new KeyValueBlock { Ansi = Ansi.For(ctx) }.Add("Contract", $"{occ}  ({contract.DisplayName})");
        object? quoteData = null, tradeData = null;
        if (quote is { } q)
        {
            quoteData = new
            {
                bid = Dec(q, "bp"), bidSize = Dec(q, "bs"), bidExchange = Str(q, "bx"),
                ask = Dec(q, "ap"), askSize = Dec(q, "as"), askExchange = Str(q, "ax"),
                condition = Str(q, "c"), timeUtc = Time(q, "t"),
            };
            block.Add("Bid", $"{Money(Dec(q, "bp"))} x {Fmt.Number(Dec(q, "bs") ?? 0)} ({Str(q, "bx") ?? "-"})")
                .Add("Ask", $"{Money(Dec(q, "ap"))} x {Fmt.Number(Dec(q, "as") ?? 0)} ({Str(q, "ax") ?? "-"})")
                .Add("Quoted at", Time(Time(q, "t")));
        }
        if (trade is { } t)
        {
            tradeData = new { price = Dec(t, "p"), size = Dec(t, "s"), exchange = Str(t, "x"), condition = Str(t, "c"), timeUtc = Time(t, "t") };
            block.Add("Last trade", $"{Money(Dec(t, "p"))} x {Fmt.Number(Dec(t, "s") ?? 0)} ({Str(t, "x") ?? "-"}, cond {Str(t, "c") ?? "-"})")
                .Add("Traded at", Time(Time(t, "t")));
        }
        block.Add("Feed", FeedName(args.Feed));
        return OperationResult.Ok(new { occ, feed = FeedName(args.Feed), quote = quoteData, trade = tradeData }, block.Render());
    }
}

/// <summary>The full snapshot for one contract (<c>/v1beta1/options/snapshots</c>): quote, trade, bars, IV and Greeks as Alpaca sends them.</summary>
[Operation("options snapshot", Summary = "Alpaca's full snapshot of one option contract: latest quote and trade, daily bars, IV and Greeks.", Risk = OperationRisk.Read)]
[Example("idiotproof options snapshot AAPL261120C00250000")]
public sealed class OptionsSnapshotOperation : IOperation<OptionsOccArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsOccArgs args, OperationContext ctx)
    {
        var contract = RequireOcc(args.Occ);
        var occ = contract.OccSymbol;
        await using var api = await DataApiAsync(ctx);
        var snap = await RequireSnapshotAsync(api, occ, args.Feed, ctx.CancellationToken);
        var q = ParseSnapshot(occ, snap);

        var block = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Contract", $"{occ}  ({contract.DisplayName}, {DaysToExpiry(contract, ctx.UtcNow)} DTE)")
            .Add("Bid / ask", $"{Money(q.Bid)} / {Money(q.Ask)}  at {Time(q.TimestampUtc)}")
            .Add("Last trade", Obj(snap, "latestTrade") is { } t ? $"{Money(Dec(t, "p"))} x {Fmt.Number(Dec(t, "s") ?? 0)} at {Time(Time(t, "t"))}" : "-")
            .Add("Today", Bar(Obj(snap, "dailyBar")))
            .Add("Prior day", Bar(Obj(snap, "prevDailyBar")))
            .Add("Minute", Bar(Obj(snap, "minuteBar")))
            .Add("IV", Iv(q.ImpliedVolatility))
            .Add("Greeks", q.Greeks is { } g
                ? $"delta {Greek(g.Delta)}  gamma {Greek(g.Gamma)}  theta {Greek(g.Theta)}  vega {Greek(g.Vega)}  rho {Greek(g.Rho)}"
                : "- (Alpaca omits them for 0DTE and when inputs are missing; `options greeks` falls back to the model)")
            .Add("Feed", FeedName(args.Feed));
        return OperationResult.Ok(new { occ, feed = FeedName(args.Feed), snapshot = snap }, block.Render());
    }

    private static string Bar(JsonElement? bar) => bar is { } b
        ? $"O {Money(Dec(b, "o"))}  H {Money(Dec(b, "h"))}  L {Money(Dec(b, "l"))}  C {Money(Dec(b, "c"))}  vol {Fmt.Number(Dec(b, "v") ?? 0)}"
        : "-";
}

/// <summary>Options exchange codes and names (<c>GET /v1beta1/options/meta/exchanges</c>).</summary>
[Operation("options exchanges", Summary = "Options exchange codes and their names, as they appear in quotes and trades.", Risk = OperationRisk.Read)]
[Example("idiotproof options exchanges")]
public sealed class OptionsExchangesOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        await using var api = await DataApiAsync(ctx);
        var root = await api.GetDataAsync("/v1beta1/options/meta/exchanges", null, ctx.CancellationToken);
        return OptionsCodes.Render(root, "Code", "Exchange", ctx);
    }
}

/// <summary>Which condition codes to list.</summary>
public enum OptionsTickType
{
    Trade,
    Quote,
}

public sealed record OptionsConditionsArgs
{
    [Option("tick", Description = "trade (default) or quote condition codes.")]
    public OptionsTickType Tick { get; init; } = OptionsTickType.Trade;
}

/// <summary>Options trade or quote condition codes (<c>GET /v1beta1/options/meta/conditions/{ticktype}</c>).</summary>
[Operation("options conditions", Summary = "Options trade or quote condition codes and what they mean.", Risk = OperationRisk.Read)]
[Example("idiotproof options conditions")]
[Example("idiotproof options conditions --tick quote")]
public sealed class OptionsConditionsOperation : IOperation<OptionsConditionsArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsConditionsArgs args, OperationContext ctx)
    {
        await using var api = await DataApiAsync(ctx);
        var tick = args.Tick == OptionsTickType.Quote ? "quote" : "trade";
        var root = await api.GetDataAsync($"/v1beta1/options/meta/conditions/{tick}", null, ctx.CancellationToken);
        return OptionsCodes.Render(root, "Code", "Meaning", ctx);
    }
}

/// <summary>Renders a code-to-name map from the options meta endpoints.</summary>
internal static class OptionsCodes
{
    public static OperationResult Render(JsonElement root, string keyHeader, string valueHeader, OperationContext ctx)
    {
        var codes = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value.ToString())
            : [];
        var table = new TextTable { MaxWidth = ctx.Width }.Column(keyHeader).Column(valueHeader);
        foreach (var (k, v) in codes) table.Row(k, v);
        return OperationResult.Ok(codes, table.Render());
    }
}
