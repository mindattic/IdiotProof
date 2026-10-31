using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketAuctionsArgs
{
    [Argument(0, Name = "symbols", Description = "One or more tickers, e.g. AAPL MSFT.")]
    public string[] Symbols { get; init; } = [];

    [Option("from", Description = "First ET date to include (default: a week ago).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET date to include (default: today).")]
    public DateOnly? To { get; init; }
}

/// <summary>
/// Opening and closing auction prints (<c>/v2/stocks/auctions</c>, SIP only): one row per symbol
/// per day with the official open and close cross price, size and time in ET.
/// </summary>
[Operation("market auctions", Summary = "Opening and closing auction prices and sizes per day for one or more stocks (SIP, times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market auctions AAPL")]
[Example("idiotproof market auctions SPY QQQ --from 2026-10-26 --to 2026-10-30")]
public sealed class MarketAuctionsOperation : IOperation<MarketAuctionsArgs>
{
    // Official cross conditions first (Q = market center official open, M = official close), then the prints.
    private static readonly string[] OpenConditions = ["Q", "O"];
    private static readonly string[] CloseConditions = ["M", "6"];

    public async Task<OperationResult> ExecuteAsync(MarketAuctionsArgs args, OperationContext ctx)
    {
        var symbols = MarketData.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol, e.g. `idiotproof market auctions AAPL`.");
        if (MarketData.Range(args.From, args.To, ctx.UtcNow, TimeSpan.FromDays(7)) is not { } range)
            return OperationResult.Usage("--from must be on or before --to, and not in the future.");

        var query = MarketData.Q(("symbols", string.Join(',', symbols)), ("start", MarketData.Rfc3339(range.StartUtc)),
            ("end", MarketData.Rfc3339(range.EndUtc)), ("feed", "sip"), ("limit", "10000"));
        await using var api = await MarketData.OpenAsync(ctx);
        var bySymbol = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        for (var page = 0; page < 20; page++)
        {
            var root = await api.GetDataAsync("/v2/stocks/auctions", query, ctx.CancellationToken);
            foreach (var (symbol, days) in MarketData.BySymbol(root, "auctions"))
            {
                if (days.ValueKind != JsonValueKind.Array) continue;
                if (!bySymbol.TryGetValue(symbol, out var list)) bySymbol[symbol] = list = [];
                list.AddRange(days.EnumerateArray());
            }
            if (MarketData.Str(root, "next_page_token") is not { Length: > 0 } token) break;
            query["page_token"] = token;
        }

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Symbol").Column("Date").Column("Open cross", Align.Right).Column("Open size", Align.Right).Column("Open time")
            .Column("Close cross", Align.Right).Column("Close size", Align.Right).Column("Close time");
        var data = new List<object>();
        foreach (var s in symbols)
        {
            foreach (var day in bySymbol.GetValueOrDefault(s) ?? [])
            {
                var date = MarketData.Str(day, "d") ?? "";
                var open = Pick(MarketData.Obj(day, "o"), OpenConditions);
                var close = Pick(MarketData.Obj(day, "c"), CloseConditions);
                DateTime? openTime = MarketData.Time(open, "t"), closeTime = MarketData.Time(close, "t");
                table.Row(s, date, MarketData.Px(MarketData.Dec(open, "p")), MarketData.Size(MarketData.Dec(open, "s")),
                    openTime is { } ot ? EasternClock.FormatTime(ot) : "-",
                    MarketData.Px(MarketData.Dec(close, "p")), MarketData.Size(MarketData.Dec(close, "s")),
                    closeTime is { } ct ? EasternClock.FormatTime(ct) : "-");
                data.Add(new
                {
                    symbol = s, date,
                    open = new { price = MarketData.Dec(open, "p"), size = MarketData.Dec(open, "s"), exchange = MarketData.Str(open, "x"), condition = MarketData.Str(open, "c"), time = MarketData.EtOffset(openTime) },
                    close = new { price = MarketData.Dec(close, "p"), size = MarketData.Dec(close, "s"), exchange = MarketData.Str(close, "x"), condition = MarketData.Str(close, "c"), time = MarketData.EtOffset(closeTime) },
                    openPrints = Prints(MarketData.Obj(day, "o")), closePrints = Prints(MarketData.Obj(day, "c")),
                });
            }
        }
        if (data.Count == 0) return OperationResult.Ok(new { auctions = data }, $"No auctions for {string.Join(", ", symbols)} in that window.");
        return OperationResult.Ok(new { auctions = data }, table.Render());
    }

    /// <summary>The official print among a day's auction entries, else the largest one.</summary>
    private static JsonElement Pick(JsonElement prints, string[] preferred)
    {
        if (prints.ValueKind != JsonValueKind.Array) return default;
        var all = prints.EnumerateArray().ToList();
        foreach (var c in preferred)
            if (all.FirstOrDefault(p => MarketData.Str(p, "c") == c) is { ValueKind: JsonValueKind.Object } hit) return hit;
        return all.OrderByDescending(p => MarketData.Dec(p, "s") ?? 0).FirstOrDefault();
    }

    private static List<object> Prints(JsonElement prints) => prints.ValueKind != JsonValueKind.Array ? [] :
        [.. prints.EnumerateArray().Select(p => (object)new
        {
            price = MarketData.Dec(p, "p"), size = MarketData.Dec(p, "s"), exchange = MarketData.Str(p, "x"),
            condition = MarketData.Str(p, "c"), time = MarketData.EtOffset(MarketData.Time(p, "t")),
        })];
}

/// <summary>Which tick type's condition codes to list.</summary>
public enum TickType
{
    Trade,
    Quote,
}

/// <summary>SIP tape: A (NYSE-listed), B (NYSE American, Arca, other regionals), C (Nasdaq-listed).</summary>
public enum Tape
{
    A,
    B,
    C,
}

public sealed record MarketConditionsArgs
{
    [Argument(0, Name = "tick-type", Description = "trade or quote.")]
    public TickType TickType { get; init; } = TickType.Trade;

    [Option("tape", Description = "SIP tape whose codes to list: a (default), b or c.")]
    public Tape Tape { get; init; } = Tape.A;
}

/// <summary>Reads Alpaca's market-data reference tables (<c>/v2/stocks/meta/...</c>).</summary>
internal static class MarketMeta
{
    public static async Task<List<(string Code, string Name)>> CodesAsync(AlpacaApi api, string path, OperationContext ctx, string? tape = null)
    {
        var root = await api.GetDataAsync(path, tape is null ? null : MarketData.Q(("tape", tape)), ctx.CancellationToken);
        return root.ValueKind != JsonValueKind.Object ? [] :
            [.. root.EnumerateObject().Select(p => (p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString()))
                .OrderBy(p => p.Item1, StringComparer.Ordinal)];
    }

    public static string Conditions(TickType type) => $"/v2/stocks/meta/conditions/{OperationParameter.EnumWireName(type)}";

    public const string Exchanges = "/v2/stocks/meta/exchanges";

    public static string Render(List<(string Code, string Name)> codes, string codeHeader, string nameHeader, OperationContext ctx)
    {
        var table = new TextTable { MaxWidth = ctx.Width }.Column(codeHeader).Column(nameHeader);
        foreach (var (code, name) in codes) table.Row(code, name);
        return codes.Count == 0 ? "(none)" : table.Render();
    }

    public static object Json(List<(string Code, string Name)> codes) => codes.Select(c => new { code = c.Code, name = c.Name }).ToList();
}

/// <summary>Trade or quote condition codes and what they mean (<c>/v2/stocks/meta/conditions/{type}</c>).</summary>
[Operation("market conditions", Summary = "What each trade or quote condition code means (the codes in market trades/quotes).", Risk = OperationRisk.Read)]
[Example("idiotproof market conditions trade")]
[Example("idiotproof market conditions quote --tape c")]
public sealed class MarketConditionsOperation : IOperation<MarketConditionsArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketConditionsArgs args, OperationContext ctx)
    {
        await using var api = await MarketData.OpenAsync(ctx);
        var tape = args.Tape.ToString();
        var codes = await MarketMeta.CodesAsync(api, MarketMeta.Conditions(args.TickType), ctx, tape);
        var type = OperationParameter.EnumWireName(args.TickType);
        return OperationResult.Ok(new { tickType = type, tape, conditions = MarketMeta.Json(codes) },
            $"{type} conditions, tape {tape}\n" + MarketMeta.Render(codes, "Code", "Meaning", ctx));
    }
}

/// <summary>Exchange codes used in trades and quotes (<c>/v2/stocks/meta/exchanges</c>).</summary>
[Operation("market exchanges", Summary = "Exchange codes used in trades and quotes and the venue each one names.", Risk = OperationRisk.Read)]
[Example("idiotproof market exchanges")]
public sealed class MarketExchangesOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        await using var api = await MarketData.OpenAsync(ctx);
        var codes = await MarketMeta.CodesAsync(api, MarketMeta.Exchanges, ctx);
        return OperationResult.Ok(new { exchanges = MarketMeta.Json(codes) }, MarketMeta.Render(codes, "Code", "Exchange", ctx));
    }
}

/// <summary>Every market-data reference table at once: exchanges, trade conditions, quote conditions.</summary>
[Operation("market meta", Summary = "All market-data reference codes at once: exchanges plus trade and quote conditions.", Risk = OperationRisk.Read)]
[Example("idiotproof market meta")]
[Example("idiotproof market meta --tape c")]
public sealed class MarketMetaOperation : IOperation<MarketMetaArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketMetaArgs args, OperationContext ctx)
    {
        await using var api = await MarketData.OpenAsync(ctx);
        var tape = args.Tape.ToString();
        var exchanges = await MarketMeta.CodesAsync(api, MarketMeta.Exchanges, ctx);
        var trade = await MarketMeta.CodesAsync(api, MarketMeta.Conditions(TickType.Trade), ctx, tape);
        var quote = await MarketMeta.CodesAsync(api, MarketMeta.Conditions(TickType.Quote), ctx, tape);
        var human = $"EXCHANGES\n{MarketMeta.Render(exchanges, "Code", "Exchange", ctx)}\n\n" +
                    $"TRADE CONDITIONS (tape {tape})\n{MarketMeta.Render(trade, "Code", "Meaning", ctx)}\n\n" +
                    $"QUOTE CONDITIONS (tape {tape})\n{MarketMeta.Render(quote, "Code", "Meaning", ctx)}";
        return OperationResult.Ok(new
        {
            tape,
            exchanges = MarketMeta.Json(exchanges),
            tradeConditions = MarketMeta.Json(trade),
            quoteConditions = MarketMeta.Json(quote),
        }, human);
    }
}

public sealed record MarketMetaArgs
{
    [Option("tape", Description = "SIP tape whose condition codes to list: a (default), b or c.")]
    public Tape Tape { get; init; } = Tape.A;
}
