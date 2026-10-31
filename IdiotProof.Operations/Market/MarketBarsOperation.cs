using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketBarsArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker, e.g. AAPL.")]
    public string Symbol { get; init; } = "";

    [Option("tf", Description = "Bar size: 1Min, 5Min, 15Min, 1Hour, 1Day (default), 1Week, 1Month; short forms 5m, 1h, 1d, 1w, 1mo work too.")]
    public string Timeframe { get; init; } = "1Day";

    [Option("from", Description = "First ET date to include (default: enough history for --limit bars).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET date to include (default: now).")]
    public DateOnly? To { get; init; }

    [Option("limit", Description = "Most bars to show (default 30, max 10000). Without --from these are the most recent.")]
    public int Limit { get; init; } = 30;

    [Option("adjustment", Description = "Corporate-action adjustment: raw, split (default), dividend or all.")]
    public string Adjustment { get; init; } = "split";
}

/// <summary>Historical OHLCV bars for one stock from <c>/v2/stocks/{symbol}/bars</c>, oldest first, times in ET.</summary>
[Operation("market bars", Summary = "Historical OHLCV bars for a stock at any timeframe, oldest first (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market bars AAPL")]
[Example("idiotproof market bars NVDA --tf 5Min --limit 50")]
[Example("idiotproof market bars MSFT --tf 1Day --from 2026-09-01 --to 2026-09-30")]
public sealed class MarketBarsOperation : IOperation<MarketBarsArgs>
{
    private static readonly string[] Adjustments = ["raw", "split", "dividend", "all"];

    public async Task<OperationResult> ExecuteAsync(MarketBarsArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof market bars AAPL`.");
        if (Timeframes.Parse(args.Timeframe) is not { } tf) return OperationResult.Usage(Timeframes.Help(args.Timeframe));
        if (args.Limit is < 1 or > 10_000) return OperationResult.Usage("--limit must be 1-10000.");
        var adjustment = args.Adjustment.Trim().ToLowerInvariant();
        if (!Adjustments.Contains(adjustment)) return OperationResult.Usage($"--adjustment must be one of {string.Join(", ", Adjustments)}.");
        if (MarketData.Range(args.From, args.To, ctx.UtcNow, tf.Lookback(args.Limit)) is not { } range)
            return OperationResult.Usage("--from must be on or before --to, and not in the future.");

        var newestFirst = args.From is null;
        var query = MarketData.Q(
            ("timeframe", tf.Wire), ("start", MarketData.Rfc3339(range.StartUtc)), ("end", MarketData.Rfc3339(range.EndUtc)),
            ("limit", Math.Min(args.Limit, 10_000).ToString(CultureInfo.InvariantCulture)), ("adjustment", adjustment),
            ("feed", MarketData.Feed), ("sort", newestFirst ? "desc" : "asc"));

        await using var api = await MarketData.OpenAsync(ctx);
        var bars = new List<JsonElement>();
        await foreach (var b in api.GetDataPagedAsync($"/v2/stocks/{Uri.EscapeDataString(symbol)}/bars", "bars", query, ct: ctx.CancellationToken))
        {
            bars.Add(b);
            if (bars.Count >= args.Limit) break;
        }
        if (newestFirst) bars.Reverse();
        if (bars.Count == 0) return OperationResult.Ok(new { symbol, timeframe = tf.Wire, feed = MarketData.Feed, bars = Array.Empty<object>() },
            $"No {tf.Wire} bars for {symbol} between {MarketData.Et(range.StartUtc)} and {MarketData.Et(range.EndUtc)}.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Time").Column("Open", Align.Right).Column("High", Align.Right).Column("Low", Align.Right)
            .Column("Close", Align.Right).Column("Chg", Align.Right).Column("Volume", Align.Right).Column("Trades", Align.Right).Column("VWAP", Align.Right);
        var data = new List<object>();
        decimal? prevClose = null;
        foreach (var b in bars)
        {
            var time = MarketData.Time(b, "t");
            decimal? o = MarketData.Dec(b, "o"), h = MarketData.Dec(b, "h"), l = MarketData.Dec(b, "l"), c = MarketData.Dec(b, "c");
            decimal? v = MarketData.Dec(b, "v"), n = MarketData.Dec(b, "n"), vw = MarketData.Dec(b, "vw");
            table.Row(tf.IsDaily ? (time is { } t ? EasternClock.FormatDate(t) : "-") : MarketData.Et(time),
                MarketData.Px(o), MarketData.Px(h), MarketData.Px(l), MarketData.Px(c),
                prevClose is null ? "" : MarketData.Signed(c - prevClose), MarketData.Compact(v), MarketData.Size(n), MarketData.Px(vw));
            data.Add(new { time = MarketData.EtOffset(time), open = o, high = h, low = l, close = c, volume = v, trades = n, vwap = vw });
            prevClose = c;
        }

        var header = $"{symbol}  {tf.Wire} bars  {bars.Count} shown  feed {MarketData.Feed}  adjustment {adjustment}";
        return OperationResult.Ok(new { symbol, timeframe = tf.Wire, feed = MarketData.Feed, adjustment, bars = data }, header + "\n" + table.Render());
    }
}

/// <summary>A parsed Alpaca timeframe (<c>5Min</c>) and how far back <c>n</c> bars of it reach.</summary>
internal sealed record Timeframe(int Amount, string Unit)
{
    public string Wire => $"{Amount}{Unit}";

    public bool IsDaily => Unit is "Day" or "Week" or "Month";

    /// <summary>
    /// A start far enough back that <paramref name="bars"/> bars exist despite nights, weekends and
    /// holidays (Alpaca then returns the newest ones because the query sorts descending).
    /// </summary>
    public TimeSpan Lookback(int bars) => Unit switch
    {
        "Min" => TimeSpan.FromMinutes(Math.Max(Amount * bars * 3.0, 0) + 4 * 24 * 60),
        "Hour" => TimeSpan.FromHours(Amount * bars * 3.5 + 96),
        "Day" => TimeSpan.FromDays(Amount * bars * 1.6 + 7),
        "Week" => TimeSpan.FromDays(Amount * bars * 7.0 + 14),
        _ => TimeSpan.FromDays(Amount * bars * 31.0 + 31),
    };
}

/// <summary>Parses user-typed timeframes (<c>5Min</c>, <c>5m</c>, <c>1h</c>, <c>1d</c>, <c>1mo</c>) for bars and forex rates.</summary>
internal static class Timeframes
{
    public static Timeframe? Parse(string? text)
    {
        var s = (text ?? "").Trim().ToLowerInvariant();
        var i = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        var amount = i == 0 ? 1 : int.TryParse(s[..i], NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : 0;
        var unit = s[i..] switch
        {
            "m" or "min" or "mins" or "minute" or "minutes" or "t" => "Min",
            "h" or "hr" or "hour" or "hours" => "Hour",
            "d" or "day" or "days" => "Day",
            "w" or "wk" or "week" or "weeks" => "Week",
            "mo" or "mon" or "month" or "months" => "Month",
            _ => null,
        };
        if (unit is null) return null;
        var ok = unit switch
        {
            "Min" => amount is >= 1 and <= 59,
            "Hour" => amount is >= 1 and <= 23,
            "Day" or "Week" => amount == 1,
            _ => amount is 1 or 2 or 3 or 4 or 6 or 12,
        };
        return ok ? new Timeframe(amount, unit) : null;
    }

    public static string Help(string text) =>
        $"Unknown timeframe '{text}'. Use [1-59]Min, [1-23]Hour, 1Day, 1Week or [1,2,3,4,6,12]Month (5m, 1h, 1d, 1w, 1mo also work).";
}
