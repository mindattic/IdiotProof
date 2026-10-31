using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketFxArgs
{
    [Argument(0, Name = "pairs", Description = "Currency pairs, e.g. USDJPY EURUSD (USD/JPY works too).")]
    public string[] Pairs { get; init; } = [];

    [Option("tf", Description = "Historical rate bar size (1Min, 5Min, 1Hour, 1Day...). Giving --tf, --from or --to switches from latest to history.")]
    public string? Timeframe { get; init; }

    [Option("from", Description = "First ET date of history (default: enough for --limit bars).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET date of history (default: now).")]
    public DateOnly? To { get; init; }

    [Option("limit", Description = "Most history bars per pair (default 30, max 1000).")]
    public int Limit { get; init; } = 30;
}

/// <summary>
/// Forex rates from Alpaca (<c>/v1beta1/forex/latest/rates</c> and <c>/v1beta1/forex/rates</c>):
/// the latest bid/mid/ask per pair by default, or OHLC rate history when a timeframe or date
/// range is given. Both render as one aligned table across every pair.
/// </summary>
[Operation("market fx", Summary = "Latest forex rates (bid/mid/ask) or historical rate bars for currency pairs (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market fx USDJPY EURUSD")]
[Example("idiotproof market fx USDJPY --tf 1Day --limit 10")]
[Example("idiotproof market fx EURUSD --tf 1Hour --from 2026-10-29 --to 2026-10-30")]
public sealed class MarketFxOperation : IOperation<MarketFxArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketFxArgs args, OperationContext ctx)
    {
        var pairs = MarketData.Symbols(args.Pairs.Select(p => p.Replace("/", "").Replace("-", "")));
        if (pairs.Count == 0) return OperationResult.Usage("Name at least one currency pair, e.g. `idiotproof market fx USDJPY`.");
        var history = args.Timeframe is not null || args.From is not null || args.To is not null;

        await using var api = await MarketData.OpenAsync(ctx);
        if (!history)
        {
            var root = await api.GetDataAsync("/v1beta1/forex/latest/rates", MarketData.Q(("currency_pairs", string.Join(',', pairs))), ctx.CancellationToken);
            var rates = MarketData.BySymbol(root, "rates");
            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Pair").Column("Bid", Align.Right).Column("Mid", Align.Right).Column("Ask", Align.Right).Column("As of");
            var data = new List<object>();
            foreach (var p in pairs)
            {
                var r = rates.GetValueOrDefault(p);
                var time = MarketData.Time(r, "t");
                table.Row(p, MarketData.Rate(MarketData.Dec(r, "bp")), MarketData.Rate(MarketData.Dec(r, "mp")), MarketData.Rate(MarketData.Dec(r, "ap")), MarketData.TickTime(time));
                data.Add(new { pair = p, bid = MarketData.Dec(r, "bp"), mid = MarketData.Dec(r, "mp"), ask = MarketData.Dec(r, "ap"), time = MarketData.EtOffset(time), found = rates.ContainsKey(p) });
            }
            return OperationResult.Ok(new { latest = data }, table.Render());
        }

        if (Timeframes.Parse(args.Timeframe ?? "1Day") is not { } tf) return OperationResult.Usage(Timeframes.Help(args.Timeframe!));
        if (args.Limit is < 1 or > 1000) return OperationResult.Usage("--limit must be 1-1000.");
        if (MarketData.Range(args.From, args.To, ctx.UtcNow, tf.Lookback(args.Limit)) is not { } range)
            return OperationResult.Usage("--from must be on or before --to, and not in the future.");

        var newestFirst = args.From is null;
        var query = MarketData.Q(("currency_pairs", string.Join(',', pairs)), ("timeframe", tf.Wire),
            ("start", MarketData.Rfc3339(range.StartUtc)), ("end", MarketData.Rfc3339(range.EndUtc)),
            ("limit", Math.Min(args.Limit * pairs.Count, 10_000).ToString(CultureInfo.InvariantCulture)), ("sort", newestFirst ? "desc" : "asc"));
        var byPair = pairs.ToDictionary(p => p, _ => new List<JsonElement>(), StringComparer.OrdinalIgnoreCase);
        for (var page = 0; page < 20 && byPair.Values.Any(l => l.Count < args.Limit); page++)
        {
            var root = await api.GetDataAsync("/v1beta1/forex/rates", query, ctx.CancellationToken);
            foreach (var (pair, bars) in MarketData.BySymbol(root, "rates"))
            {
                if (bars.ValueKind != JsonValueKind.Array || !byPair.TryGetValue(pair, out var list)) continue;
                foreach (var b in bars.EnumerateArray())
                    if (list.Count < args.Limit) list.Add(b);
            }
            if (MarketData.Str(root, "next_page_token") is not { Length: > 0 } token) break;
            query["page_token"] = token;
        }

        var hist = new TextTable { MaxWidth = ctx.Width }
            .Column("Pair").Column("Time").Column("Open", Align.Right).Column("High", Align.Right).Column("Low", Align.Right).Column("Close", Align.Right);
        var series = new List<object>();
        foreach (var p in pairs)
        {
            var bars = byPair[p];
            if (newestFirst) bars.Reverse();
            var rows = new List<object>();
            foreach (var b in bars)
            {
                var time = MarketData.Time(b, "t");
                decimal? o = MarketData.Dec(b, "o"), h = MarketData.Dec(b, "h"), l = MarketData.Dec(b, "l"), c = MarketData.Dec(b, "c");
                hist.Row(p, tf.IsDaily ? (time is { } t ? EasternClock.FormatDate(t) : "-") : MarketData.Et(time),
                    MarketData.Rate(o), MarketData.Rate(h), MarketData.Rate(l), MarketData.Rate(c));
                rows.Add(new { time = MarketData.EtOffset(time), open = o, high = h, low = l, close = c });
            }
            series.Add(new { pair = p, bars = rows });
        }
        var human = hist.RowCount == 0 ? $"No {tf.Wire} rates for {string.Join(", ", pairs)} in that window." : $"{tf.Wire} rates\n{hist.Render()}";
        return OperationResult.Ok(new { timeframe = tf.Wire, history = series }, human);
    }
}
