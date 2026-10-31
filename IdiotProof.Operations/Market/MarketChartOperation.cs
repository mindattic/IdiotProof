using System.Globalization;
using IdiotProof.DataFeeds;
using IdiotProof.Operations.Rendering;
using MindAttic.Helpers.Ascii;

namespace IdiotProof.Operations.Market;

public sealed record MarketChartArgs
{
    [Argument(0, Name = "symbol", Description = "Ticker to chart, e.g. INTC.")]
    public string Symbol { get; init; } = "";

    [Option("days", Description = "Trading days to show, oldest first, each as premarket + regular + after-hours (default 2: the prior day and today).")]
    public int Days { get; init; } = 2;

    [Option("bucket", Description = "Minutes per time slot: half a character in Braille, one in --ascii (default: the smallest that fits the width).")]
    public int? Bucket { get; init; }

    [Option("width", Description = "Chart width in characters (default: the terminal's, else 118).")]
    public int? Width { get; init; }

    [Option("height", Description = "Price pane rows (default 20).")]
    public int Height { get; init; } = 20;

    [Option("volume", Description = "Show the volume pane (default on; --no-volume hides it).")]
    public bool Volume { get; init; } = true;

    [Option("ascii", Description = "Pure-ASCII candles (one per character) instead of the high-resolution Braille dots.")]
    public bool Ascii { get; init; }
}

/// <summary>One trading session's summary in the chart's payload; times are ET.</summary>
public sealed record ChartSessionSummary(
    string Day, string Session, string StartEt, string EndEt,
    decimal? Open, decimal? High, decimal? Low, decimal? Close, decimal Volume, int Bars);

/// <summary>
/// Pulls 1-minute bars through the Hub's market data feed and draws them as an ASCII candle chart
/// across each trading day's premarket (4:00-9:30), regular (9:30-4:00) and after-hours
/// (4:00-8:00) sessions in ET, with every prior regular close as a reference line. The overnight
/// gap is not drawn. Refuses the Mock feed: a chart of made-up prices is worse than none.
/// </summary>
[Operation("market chart", Summary = "ASCII candle chart of a symbol across premarket, regular and after-hours sessions (ET).", Risk = OperationRisk.Read)]
[Example("idiotproof market chart INTC")]
[Example("idiotproof market chart NVDA --days 3 --bucket 30")]
public sealed class MarketChartOperation : IOperation<MarketChartArgs>
{
    private static readonly (string Label, TimeSpan Start, TimeSpan End)[] DaySessions =
    [
        ("PRE", new(4, 0, 0), new(9, 30, 0)),
        ("REGULAR", new(9, 30, 0), new(16, 0, 0)),
        ("AFTER", new(16, 0, 0), new(20, 0, 0)),
    ];

    public async Task<OperationResult> ExecuteAsync(MarketChartArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol, e.g. `idiotproof market chart INTC`.");
        if (args.Days is < 1 or > 10) return OperationResult.Usage("--days must be 1-10.");
        if (args.Height is < 5 or > 60) return OperationResult.Usage("--height must be 5-60.");

        var feed = ctx.Get<IMarketDataFeed>();
        if (feed is MockDataFeed)
            return OperationResult.Blocked("The Hub is on the Mock data feed (no Alpaca keys resolved), so there are no real prices to chart.");

        // Fetch far enough back to cover weekends and holidays, then keep the last N ET days that traded.
        var nowUtc = ctx.UtcNow;
        var fromUtc = nowUtc.AddDays(-(args.Days + 6));
        var candles = new List<IdiotProof.Models.Candle>();
        await foreach (var c in feed.GetHistoricalCandlesAsync(symbol, fromUtc, nowUtc, TimeSpan.FromMinutes(1), ctx.CancellationToken))
            candles.Add(c);
        if (candles.Count == 0) return OperationResult.Fail($"No bars for {symbol} from {feed.FeedName} in the last {args.Days + 6} days.");

        var bars = candles
            .Select(c => new OhlcBar(EasternClock.ToEastern(c.StartUtc), (double)c.Open, (double)c.High, (double)c.Low, (double)c.Close, (double)c.Volume))
            .ToList();
        var days = bars.Select(b => b.Time.Date).Distinct().OrderBy(d => d).TakeLast(args.Days).ToList();

        var sessions = days
            .SelectMany(d => DaySessions.Select(s => new ChartSession(s.Label, d + s.Start, d + s.End, d.ToString("ddd M/d", CultureInfo.InvariantCulture))))
            .ToList();

        // Reference lines: the regular close of every shown day that has a later day after it.
        var references = new List<ChartReferenceLine>();
        foreach (var d in days.SkipLast(1))
        {
            var close = bars.LastOrDefault(b => b.Time >= d + DaySessions[1].Start && b.Time < d + DaySessions[1].End);
            if (close != default) references.Add(new ChartReferenceLine($"{d:ddd} close", close.Close));
        }

        var shown = bars.Where(b => sessions.Any(s => b.Time >= s.Start && b.Time < s.End)).ToList();
        var first = shown.FirstOrDefault();
        var last = shown.LastOrDefault();
        var title = $"{symbol}  {days[0]:ddd M/d} 4:00 AM -> {days[^1]:ddd M/d} 8:00 PM ET   " +
                    $"last {Price(last.Close)}  ({Signed(last.Close - first.Open)} / {Signed((last.Close - first.Open) / first.Open * 100)}% since {first.Time:ddd h:mm tt})   feed {feed.FeedName}";

        string chart;
        try
        {
            chart = AsciiCandleChart.Render(bars, sessions, new CandleChartOptions
            {
                Title = title,
                Mode = args.Ascii ? CandleChartMode.Candles : CandleChartMode.Braille,
                Width = args.Width ?? ctx.Width ?? 118,
                Height = args.Height,
                VolumeHeight = args.Volume ? 4 : 0,
                BucketSize = args.Bucket is { } m ? TimeSpan.FromMinutes(m) : null,
                PriceFormat = last.Close < 1 ? "0.0000" : "0.00",
                ReferenceLines = references,
            });
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Usage(ex.Message);
        }

        var summaries = sessions.Select(s =>
        {
            var inSession = bars.Where(b => b.Time >= s.Start && b.Time < s.End).ToList();
            return new ChartSessionSummary(
                s.Group, s.Label, s.Start.ToString("h:mm tt", CultureInfo.InvariantCulture) + " ET", s.End.ToString("h:mm tt", CultureInfo.InvariantCulture) + " ET",
                inSession.Count > 0 ? (decimal)inSession[0].Open : null,
                inSession.Count > 0 ? (decimal)inSession.Max(b => b.High) : null,
                inSession.Count > 0 ? (decimal)inSession.Min(b => b.Low) : null,
                inSession.Count > 0 ? (decimal)inSession[^1].Close : null,
                (decimal)inSession.Sum(b => b.Volume), inSession.Count);
        }).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Day").Column("Session").Column("Open", Align.Right).Column("High", Align.Right)
            .Column("Low", Align.Right).Column("Close", Align.Right).Column("Chg", Align.Right).Column("Volume", Align.Right);
        foreach (var s in summaries)
        {
            if (s.Bars == 0) { table.Row(s.Day, s.Session, "-", "-", "-", "-", "-", "0"); continue; }
            table.Row(s.Day, s.Session, Price((double)s.Open!), Price((double)s.High!), Price((double)s.Low!),
                Price((double)s.Close!), Signed((double)(s.Close! - s.Open!)), CompactVolume((double)s.Volume));
        }

        var data = new { symbol, feed = feed.FeedName, days = days.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), sessions = summaries, chart };
        return OperationResult.Ok(data, chart + "\n\n" + table.Render());
    }

    private static string Price(double v) => v.ToString(v < 1 ? "0.0000" : "0.00", CultureInfo.InvariantCulture);

    private static string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string CompactVolume(double v) => v switch
    {
        >= 1e9 => (v / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "B",
        >= 1e6 => (v / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 1e3 => (v / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        _ => v.ToString("0", CultureInfo.InvariantCulture),
    };
}
