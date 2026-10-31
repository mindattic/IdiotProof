using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdiotProof.Operations.Market;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Streaming;

public sealed record MarketWatchArgs
{
    [Argument(0, Name = "symbols", Required = false, Description = "Stocks (AAPL), crypto pairs (BTC/USD) and option contracts (AAPL261120C00200000), mixed freely.")]
    public string[] Symbols { get; init; } = [];

    [Option("channels", Description = "trades, quotes, bars, daily-bars, updated-bars, statuses, lulds (stocks only: statuses, lulds; options: trades, quotes). Default trades,quotes.")]
    public string[]? Channels { get; init; }

    [Option("news", Description = "Also stream news headlines for these symbols (all news when no symbol is given).")]
    public bool News { get; init; }

    [Option("feed", Description = "Stock feed: sip, iex or delayed_sip (default: the Hub's market data tier).")]
    public string? Feed { get; init; }

    [Option("options-feed", Description = "Options feed: opra or indicative (default opra).")]
    public string OptionsFeed { get; init; } = "opra";

    [Option("seconds", Description = "Stop after this many seconds (a terminal streams until Ctrl+C; MCP/--json default 15, at most 300).")]
    public int? Seconds { get; init; }

    [Option("max", Description = "Stop after this many lines.")]
    public int? Max { get; init; }
}

/// <summary>
/// <c>market watch</c> (IP-T326): a live tape from Alpaca's market data websockets through the
/// Hub: stock trades, quotes, bars, trading statuses and LULD bands; crypto trades, quotes and
/// bars; option trades and quotes (the options feed speaks msgpack); news headlines. One websocket
/// per asset class, all merged into one stream of ET-stamped lines. Read-only.
/// </summary>
[Operation("market watch", Summary = "Live market tape over Alpaca's websockets: stock trades/quotes/bars/statuses/LULD, crypto, options and news, merged into one ET-stamped stream.", Risk = OperationRisk.Read,
    Description = "A terminal streams until Ctrl+C; MCP and --json get a bounded window (--seconds, default 15). Alpaca limits stock-feed connections per account: if the Monitor's stream holds the SIP connection, try --feed iex.")]
[Streaming]
[Example("idiotproof market watch AAPL MSFT")]
[Example("idiotproof market watch NVDA --channels trades,quotes,statuses,lulds")]
[Example("idiotproof market watch BTC/USD ETH/USD --channels trades,bars")]
[Example("idiotproof market watch AAPL261120C00200000 --channels quotes")]
[Example("idiotproof market watch --news")]
public sealed partial class MarketWatchOperation : IOperation<MarketWatchArgs>
{
    /// <summary>CLI channel names and the subscribe-message keys they become.</summary>
    internal static readonly Dictionary<string, string> ChannelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trades"] = "trades", ["quotes"] = "quotes", ["bars"] = "bars", ["daily-bars"] = "dailyBars",
        ["updated-bars"] = "updatedBars", ["statuses"] = "statuses", ["lulds"] = "lulds",
    };

    private static readonly HashSet<string> CryptoChannels = ["trades", "quotes", "bars", "dailyBars", "updatedBars"];
    private static readonly HashSet<string> OptionChannels = ["trades", "quotes"];

    public async Task<OperationResult> ExecuteAsync(MarketWatchArgs args, OperationContext ctx)
    {
        var symbols = args.Symbols.SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        if (symbols.Count == 0 && !args.News) return OperationResult.Usage("Name at least one symbol, or --news.");
        if (symbols.Count > 200) return OperationResult.Usage("At most 200 symbols per watch.");
        if (args.Seconds is < 1 or > 86_400) return OperationResult.Usage("--seconds must be 1-86400.");

        var requested = (args.Channels is { Length: > 0 } c ? c : ["trades", "quotes"])
            .SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        var unknown = requested.Where(x => !ChannelKeys.ContainsKey(x)).ToList();
        if (unknown.Count > 0) return OperationResult.Usage($"Unknown channel(s) {string.Join(", ", unknown)}. One of: {string.Join(", ", ChannelKeys.Keys)}.");
        var channels = requested.Select(x => ChannelKeys[x]).Distinct().ToList();

        var feed = (args.Feed ?? MarketData.Feed).Trim().ToLowerInvariant();
        if (feed is not ("sip" or "iex" or "delayed_sip")) return OperationResult.Usage("--feed is sip, iex or delayed_sip.");
        var optionsFeed = args.OptionsFeed.Trim().ToLowerInvariant();
        if (optionsFeed is not ("opra" or "indicative")) return OperationResult.Usage("--options-feed is opra or indicative.");

        var crypto = symbols.Where(s => s.Contains('/')).ToList();
        var options = symbols.Where(s => !s.Contains('/') && OccSymbol().IsMatch(s)).ToList();
        var stocks = symbols.Except(crypto).Except(options).ToList();

        var plans = new List<WatchPlan>();
        if (stocks.Count > 0) plans.Add(new WatchPlan($"stocks · {feed}", $"v2/{feed}", Subscribe(channels, stocks), false));
        if (crypto.Count > 0)
        {
            var cc = channels.Where(CryptoChannels.Contains).ToList();
            if (cc.Count == 0) return OperationResult.Usage("Crypto streams trades, quotes, bars, daily-bars or updated-bars.");
            plans.Add(new WatchPlan("crypto", "v1beta3/crypto/us", Subscribe(cc, crypto), false));
        }
        if (options.Count > 0)
        {
            var oc = channels.Where(OptionChannels.Contains).ToList();
            if (oc.Count == 0) return OperationResult.Usage("Options stream trades or quotes.");
            plans.Add(new WatchPlan($"options · {optionsFeed}", $"v1beta1/{optionsFeed}", Subscribe(oc, options), true));
        }
        if (args.News)
            plans.Add(new WatchPlan("news", "v1beta1/news",
                new Dictionary<string, object?> { ["action"] = "subscribe", ["news"] = stocks.Count > 0 ? stocks.ToArray() : new[] { "*" } }, false));

        var keys = await ctx.Get<AlpacaStreamCredentials>().ForDataAsync(ctx.OwnerId, ctx.CancellationToken);
        var registry = ctx.Get<StreamRegistry>();
        using var stream = OperationStream.Open(ctx, args.Seconds, args.Max);
        if (stream.Live)
            await stream.EmitAsync($"Watching {string.Join(", ", plans.Select(p => p.Name))}: {string.Join(" ", symbols.DefaultIfEmpty("all news"))} (Ctrl+C stops)");

        var counts = new Dictionary<string, int>();
        var opened = plans.Select(p =>
        {
            var name = p.Name;
            var watch = registry.OpenWatch($"market watch · {name}", new Uri(AlpacaStreamSession.DataStreamBase + p.Path), keys,
                () => p.Subscribe, p.MsgPack, () => { lock (counts) return $"{(counts.TryGetValue(name, out var n) ? n : 0)} line(s) shown"; }, ctx.OwnerId);
            return (Plan: p, watch.Session, watch.Handle);
        }).ToList();
        var refused = new List<string>();
        try
        {
            await stream.RunAsync(ct => Task.WhenAll(opened.Select(async w =>
            {
                try
                {
                    await w.Session.RunAsync(async message =>
                    {
                        if (Render(message, w.Plan.Name) is not { } line) return;
                        lock (counts) counts[w.Plan.Name] = counts.GetValueOrDefault(w.Plan.Name) + 1;
                        await stream.EmitAsync(line, message);
                    }, ct);
                }
                catch (AlpacaStreamRefusedException ex)
                {
                    lock (refused) refused.Add(ex.Message);
                    await stream.EmitAsync(ex.Message);
                }
            })));
        }
        finally
        {
            foreach (var w in opened) w.Handle.Dispose();
        }

        if (refused.Count == opened.Count && stream.Count == refused.Count && !stream.Live)
            return OperationResult.Blocked(string.Join(" ", refused));
        return stream.Finish($"Market watch: {string.Join(", ", plans.Select(p => p.Name))} · {string.Join(" ", symbols.DefaultIfEmpty("all news"))}",
            new { Symbols = symbols, Streams = plans.Select(p => p.Name), Channels = channels, Refused = refused });
    }

    private static Dictionary<string, object?> Subscribe(IEnumerable<string> channels, List<string> symbols)
    {
        var message = new Dictionary<string, object?> { ["action"] = "subscribe" };
        foreach (var c in channels) message[c] = symbols.ToArray();
        return message;
    }

    private sealed record WatchPlan(string Name, string Path, Dictionary<string, object?> Subscribe, bool MsgPack);

    /// <summary>One market data message as one tape line, or null for protocol chatter.</summary>
    internal static string? Render(JsonElement m, string stream)
    {
        var type = AlpacaStreamSession.Str(m, "T");
        var symbol = AlpacaStreamSession.Str(m, "S") ?? "";
        var at = AlpacaStreamSession.Str(m, "t") is { } t && DateTime.TryParse(t, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? EasternClock.ToEastern(parsed).ToString("h:mm:ss tt", CultureInfo.InvariantCulture) + " ET"
            : "";
        string Line(string kind, string text) => $"{at,-14} {symbol,-20} {kind,-8} {text}";
        return type switch
        {
            "t" => Line("trade", $"{Px(m, "p")} x {Qty(m, "s")}" + Opt(m, "x", " ")),
            "q" => Line("quote", $"bid {Px(m, "bp")} x {Qty(m, "bs")}  ask {Px(m, "ap")} x {Qty(m, "as")}"),
            "b" or "d" or "u" => Line(type == "b" ? "bar" : type == "d" ? "daily" : "updated",
                $"O {Px(m, "o")} H {Px(m, "h")} L {Px(m, "l")} C {Px(m, "c")} V {Qty(m, "v")}"),
            "s" => Line("status", $"{AlpacaStreamSession.Str(m, "sc")} {AlpacaStreamSession.Str(m, "sm")}".Trim() +
                (AlpacaStreamSession.Str(m, "rm") is { Length: > 0 } reason ? $" ({reason})" : "")),
            "l" => Line("LULD", $"up {Px(m, "u")} down {Px(m, "d")}" + Opt(m, "i", " indicator ")),
            "c" => Line("correct", $"{Px(m, "op")} x {Qty(m, "os")} -> {Px(m, "cp")} x {Qty(m, "cs")}"),
            "x" => Line("cancel", $"{AlpacaStreamSession.Str(m, "a") ?? "cancel"} {Px(m, "p")} x {Qty(m, "s")}"),
            "n" => $"{at,-14} {"news",-20} {AlpacaStreamSession.Str(m, "headline")}" + NewsSymbols(m) + Opt(m, "source", " · "),
            "subscription" => $"{"",-14} {stream,-20} {"subscribed",-8} " + Subscribed(m),
            "error" => $"{"",-14} {stream,-20} {"error",-8} {AlpacaStreamSession.Str(m, "msg")}",
            _ => null,
        };
    }

    private static string Px(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? Fmt.Money(d, d < 1 ? 4 : 2) : "-";

    private static string Qty(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? Fmt.Number(d) : "-";

    private static string Opt(JsonElement m, string name, string prefix) =>
        m.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? prefix + v.ToString() : "";

    private static string NewsSymbols(JsonElement m) =>
        m.TryGetProperty("symbols", out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0
            ? $" [{string.Join(", ", s.EnumerateArray().Select(x => x.GetString()).Take(8))}]"
            : "";

    private static string Subscribed(JsonElement m) =>
        string.Join("; ", m.EnumerateObject()
            .Where(p => p.Name != "T" && p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0)
            .Select(p => $"{p.Name} {string.Join(",", p.Value.EnumerateArray().Select(x => x.GetString()))}"));

    [GeneratedRegex(@"^[A-Z]{1,6}\d{6}[CP]\d{8}$")]
    private static partial Regex OccSymbol();
}
