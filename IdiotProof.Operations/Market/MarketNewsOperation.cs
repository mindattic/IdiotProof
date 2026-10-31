using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Market;

public sealed record MarketNewsArgs
{
    [Argument(0, Name = "symbols", Description = "Tickers to filter on, e.g. AAPL TSLA (none = all market news).", Required = false)]
    public string[] Symbols { get; init; } = [];

    [Option("limit", Description = "Most articles to show, newest first (default 10, max 200).")]
    public int Limit { get; init; } = 10;

    [Option("from", Description = "Earliest ET date to include (default: no bound).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Latest ET date to include (default: now).")]
    public DateOnly? To { get; init; }

    [Option("summary", Description = "Print each article's summary and link under its headline.")]
    public bool Summary { get; init; }
}

/// <summary>
/// Alpaca news (<c>/v1beta1/news</c>: Benzinga and other wires) newest first. Calls the API
/// through <see cref="IdiotProof.Brokers.AlpacaApi"/> rather than AlpacaNewsService, which
/// swallows errors, ignores the owner's keys and has no limit/date knobs.
/// </summary>
[Operation("market news", Summary = "Latest news headlines for one or more stocks, or the whole market (times in ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market news AAPL")]
[Example("idiotproof market news TSLA NVDA --limit 25 --summary")]
[Example("idiotproof market news --from 2026-10-29")]
public sealed class MarketNewsOperation : IOperation<MarketNewsArgs>
{
    public async Task<OperationResult> ExecuteAsync(MarketNewsArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 200) return OperationResult.Usage("--limit must be 1-200.");
        if (args.From is { } f && args.To is { } t && f > t) return OperationResult.Usage("--from must be on or before --to.");
        var symbols = MarketData.Symbols(args.Symbols);

        var query = MarketData.Q(
            ("symbols", symbols.Count > 0 ? string.Join(',', symbols) : null),
            ("limit", Math.Min(args.Limit, 50).ToString(CultureInfo.InvariantCulture)),
            ("sort", "desc"),
            ("include_content", "false"),
            ("start", args.From is { } from ? MarketData.Rfc3339(MarketData.EtDayStartUtc(from)) : null),
            ("end", args.To is { } to ? MarketData.Rfc3339(Min(MarketData.EtDayStartUtc(to.AddDays(1)), ctx.UtcNow)) : null));

        await using var api = await MarketData.OpenAsync(ctx);
        var articles = new List<JsonElement>();
        await foreach (var a in api.GetDataPagedAsync("/v1beta1/news", "news", query, ct: ctx.CancellationToken))
        {
            articles.Add(a);
            if (articles.Count >= args.Limit) break;
        }
        var scope = symbols.Count > 0 ? string.Join(", ", symbols) : "the market";
        if (articles.Count == 0) return OperationResult.Ok(new { symbols, articles = Array.Empty<object>() }, $"No news for {scope}.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Published").Column("Source").Column("Symbols", maxWidth: 24).Column("Headline");
        var data = new List<object>();
        var blocks = new List<string>();
        foreach (var a in articles)
        {
            var created = MarketData.Time(a, "created_at");
            var tickers = MarketData.Strings(a, "symbols");
            string headline = MarketData.Str(a, "headline") ?? "", source = MarketData.Str(a, "source") ?? "";
            string? summary = MarketData.Str(a, "summary"), url = MarketData.Str(a, "url");
            table.Row(MarketData.Et(created), source, string.Join(",", tickers), headline);
            if (args.Summary)
            {
                var block = new KeyValueBlock()
                    .Add("Published", MarketData.Et(created)).Add("Source", source).Add("Symbols", string.Join(", ", tickers))
                    .Add("Headline", headline).Add("Summary", string.IsNullOrWhiteSpace(summary) ? null : summary).Add("Link", url);
                blocks.Add(block.Render());
            }
            data.Add(new
            {
                id = a.TryGetProperty("id", out var id) ? id.ToString() : null,
                published = MarketData.EtOffset(created), updated = MarketData.EtOffset(MarketData.Time(a, "updated_at")),
                source, author = MarketData.Str(a, "author"), symbols = tickers, headline, summary, url,
            });
        }
        var header = $"News for {scope} ({articles.Count}, newest first)";
        var human = header + "\n" + (args.Summary ? string.Join("\n\n", blocks) : table.Render());
        return OperationResult.Ok(new { symbols, articles = data }, human);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
