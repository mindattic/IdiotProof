using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using MindAttic.Helpers.Ascii;

namespace IdiotProof.Operations.Account;

/// <summary>One point of <c>/v2/account/portfolio/history</c>: equity and the cumulative P&amp;L since the base value (deposits excluded).</summary>
public sealed record PortfolioPoint(DateTime Utc, decimal Equity, decimal? ProfitLoss);

/// <summary>A parsed portfolio history: the points with an equity value, the base they are measured from, and the timeframe Alpaca used.</summary>
public sealed record PortfolioHistory(IReadOnlyList<PortfolioPoint> Points, decimal? BaseValue, string? Timeframe)
{
    /// <summary>The P&amp;L over the window from Alpaca's <c>profit_loss</c>, which excludes deposits and withdrawals.</summary>
    public decimal? ProfitLoss => Points.LastOrDefault(p => p.ProfitLoss is not null)?.ProfitLoss;

    /// <summary><see cref="ProfitLoss"/> as a fraction of <see cref="BaseValue"/>.</summary>
    public decimal? ProfitLossPct => ProfitLoss is { } pl && BaseValue is { } b && b != 0 ? pl / b : null;

    public static PortfolioHistory Parse(JsonElement root)
    {
        var points = new List<PortfolioPoint>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Array
            && root.TryGetProperty("equity", out var eq) && eq.ValueKind == JsonValueKind.Array)
        {
            var pl = root.TryGetProperty("profit_loss", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().ToList() : [];
            var times = ts.EnumerateArray().ToList();
            var equities = eq.EnumerateArray().ToList();
            for (int i = 0; i < Math.Min(times.Count, equities.Count); i++)
            {
                if (times[i].ValueKind != JsonValueKind.Number || AccountApis.Dec(equities[i]) is not { } e) continue;
                points.Add(new PortfolioPoint(DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()).UtcDateTime, e,
                    i < pl.Count ? AccountApis.Dec(pl[i]) : null));
            }
        }
        return new PortfolioHistory(points, AccountApis.Dec(root, "base_value"), AccountApis.Str(root, "timeframe"));
    }

    /// <summary>
    /// Today's true P&amp;L: the 1-day history (extended hours, P&amp;L reset per day), whose
    /// <c>profit_loss</c> leaves out deposits and withdrawals, unlike equity minus last_equity.
    /// </summary>
    public static async Task<PortfolioHistory> TodayAsync(AlpacaApi api, CancellationToken ct) =>
        Parse(await api.GetAsync("/v2/account/portfolio/history", new Dictionary<string, string?>
        {
            ["period"] = "1D",
            ["timeframe"] = "15Min",
            ["intraday_reporting"] = "extended_hours",
            ["pnl_reset"] = "per_day",
            // Without this Alpaca counts deposits and withdrawals as profit_loss.
            ["cashflow_types"] = "ALL",
        }, ct));
}

public sealed record AccountHistoryArgs
{
    [Option("period", Description = "Window: <n>D, <n>W, <n>M or <n>A (years), or all (default 1M).")]
    public string Period { get; init; } = "1M";

    [Option("timeframe", Description = "Point spacing: 1Min, 5Min, 15Min, 1H or 1D (default: Alpaca's choice for the period).")]
    public string? Timeframe { get; init; }

    [Option("extended", Description = "Include premarket and after-hours in intraday history (default regular hours only).")]
    public bool Extended { get; init; }

    [Option("mode", Description = "paper or live (default paper).")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("width", Description = "Chart width in characters (default: the terminal's, else 118).")]
    public int? Width { get; init; }

    [Option("height", Description = "Chart rows (default 16).")]
    public int Height { get; init; } = 16;
}

/// <summary>
/// The account's equity over time (<c>GET /v2/account/portfolio/history</c>) as a Braille line chart,
/// with the window's true P&amp;L from Alpaca's <c>profit_loss</c> (deposits and withdrawals excluded)
/// and the net cash moved in or out. Intraday windows are drawn one ET day per session.
/// </summary>
[Operation("account history", Summary = "Equity over time as a Braille line chart, with the true P&L (deposits excluded) for 1D to all-time windows.", Risk = OperationRisk.Read)]
[Example("idiotproof account history")]
[Example("idiotproof account history --period 1D --timeframe 5Min --extended")]
[Example("idiotproof account history --period 1A --mode live")]
public sealed partial class AccountHistoryOperation : IOperation<AccountHistoryArgs>
{
    private static readonly string[] Timeframes = ["1Min", "5Min", "15Min", "1H", "1D"];

    [GeneratedRegex("^[1-9][0-9]{0,3}[DWMA]$")]
    private static partial Regex PeriodPattern();

    public async Task<OperationResult> ExecuteAsync(AccountHistoryArgs args, OperationContext ctx)
    {
        var period = args.Period.Trim().ToUpperInvariant();
        var all = period == "ALL";
        if (!all && !PeriodPattern().IsMatch(period)) return OperationResult.Usage("--period is <n>D, <n>W, <n>M, <n>A or all (e.g. 1D, 2W, 3M, 1A).");
        string? timeframe = null;
        if (args.Timeframe is { } tf)
        {
            timeframe = Timeframes.FirstOrDefault(t => string.Equals(t, tf.Trim(), StringComparison.OrdinalIgnoreCase));
            if (timeframe is null) return OperationResult.Usage($"--timeframe is one of {string.Join(", ", Timeframes)}.");
        }
        if (args.Height is < 5 or > 60) return OperationResult.Usage("--height must be 5-60.");
        var width = args.Width ?? ctx.Width ?? 118;
        if (width < 60) return OperationResult.Usage("--width must be at least 60.");

        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(ctx.RequireOwner(), args.Mode, ctx.CancellationToken);
        var query = new Dictionary<string, string?>
        {
            ["period"] = all ? null : period,
            ["timeframe"] = timeframe,
            ["intraday_reporting"] = args.Extended ? "extended_hours" : "market_hours",
            ["extended_hours"] = args.Extended ? "true" : null,
            ["cashflow_types"] = "ALL", // deposits/withdrawals are cash flow, not profit_loss
        };
        if (all)
        {
            var account = await api.GetAsync("/v2/account", ct: ctx.CancellationToken);
            var opened = AccountApis.Instant(account, "created_at") ?? ctx.UtcNow.AddYears(-1);
            query["start"] = opened.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            query["end"] = ctx.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            query["timeframe"] ??= "1D";
        }
        var history = PortfolioHistory.Parse(await api.GetAsync("/v2/account/portfolio/history", query, ctx.CancellationToken));
        var name = args.Mode.ToString();
        if (history.Points.Count == 0)
            return OperationResult.Ok(new { account = name.ToLowerInvariant(), period, points = Array.Empty<object>() }, $"{name}: no portfolio history for {period}.");

        var points = history.Points;
        var first = points[0];
        var last = points[^1];
        // P&L = equity change minus deposits/withdrawals in the window (from account activities):
        // Alpaca's profit_loss counts an instant ACH deposit as a gain.
        var startEquity = history.BaseValue ?? first.Equity;
        // Without base_value the start IS the first point, whose equity already includes that
        // day's deposits on a daily series: count cash flows only after it, or a day-1 deposit is
        // subtracted twice.
        var flowsFrom = history.BaseValue is null && points.Count > 1 && points[1].Utc - first.Utc >= TimeSpan.FromDays(1)
            ? AccountApis.EtDate(first.Utc).AddDays(1)
            : AccountApis.EtDate(first.Utc);
        var flowsTo = AccountApis.EtDate(last.Utc);
        decimal? cashFlow = flowsFrom > flowsTo ? 0m : await AccountApis.NetCashFlowAsync(api, flowsFrom, flowsTo, ctx.CancellationToken);
        decimal? pnl = last.Equity - startEquity - cashFlow;
        var capital = startEquity + (cashFlow ?? 0); // deposits are capital at work, not a gain
        decimal? pnlPct = capital != 0 ? pnl / capital : null;
        var used = timeframe ?? history.Timeframe;
        var daily = used is "1D" || (used is null && points.Count > 1 && points[1].Utc - points[0].Utc >= TimeSpan.FromDays(1));
        var title = $"{name} equity  {period}  {EasternClock.FormatDate(first.Utc)} -> {EasternClock.FormatDate(last.Utc)} ET   " +
                    $"P&L {AccountApis.Signed(pnl)}" + (pnlPct is { } pct ? $" ({Fmt.Percent(pct, 2, signed: true)})" : "") + " excl. deposits";

        string chart;
        try
        {
            chart = EquityChart.Render(points.Select(pt => (EasternClock.ToEastern(pt.Utc), (double)pt.Equity)).ToList(),
                daily, (double?)history.BaseValue, title, width, args.Height);
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Usage(ex.Message);
        }

        var block = new KeyValueBlock()
            .Add("Start equity", Fmt.Money(history.BaseValue ?? first.Equity))
            .Add("End equity", Fmt.Money(last.Equity) + $"  ({(daily ? EasternClock.FormatDate(last.Utc) : Fmt.Et(last.Utc))})")
            .Add("P&L", AccountApis.Signed(pnl) + (pnlPct is { } pp ? $"  ({Fmt.Percent(pp, 2, signed: true)})" : "") + "  from trading, deposits excluded")
            .Add("Deposits/withdrawals", cashFlow is { } c && Math.Abs(c) >= 0.01m ? Fmt.SignedMoney(c) : null)
            .Add("High", Fmt.Money(points.Max(x => x.Equity)))
            .Add("Low", Fmt.Money(points.Min(x => x.Equity)));

        var data = new
        {
            account = name.ToLowerInvariant(), period, timeframe = timeframe ?? history.Timeframe, extended = args.Extended,
            baseValue = history.BaseValue, startEquity = history.BaseValue ?? first.Equity, endEquity = last.Equity,
            profitLoss = pnl, profitLossPct = pnlPct, netDeposits = cashFlow,
            points = points.Select(x => new { timeEt = Fmt.Et(x.Utc), utc = x.Utc, equity = x.Equity, profitLoss = x.ProfitLoss }),
            chart,
        };
        return OperationResult.Ok(data, chart + "\n\n" + block.Render());
    }
}

/// <summary>
/// Draws an equity series as a continuous Braille line on <see cref="AsciiCandleChart"/>: the series is
/// resampled to one point per time slot by linear interpolation, and each slot's bar spans from the
/// previous value to its own, so consecutive dots join. Intraday series get one session per ET day
/// (overnight gaps are not drawn); daily series are one session with date ticks.
/// </summary>
internal static class EquityChart
{
    public static string Render(IReadOnlyList<(DateTime Et, double Value)> series, bool daily, double? baseValue, string title, int width, int height)
    {
        List<ChartSession> sessions = daily
            ? [new ChartSession("", series[0].Et.Date, series[^1].Et.Date.AddDays(1))]
            : series.GroupBy(x => x.Et.Date).Select(g =>
            {
                var start = g.First().Et;
                var end = g.Last().Et;
                return new ChartSession(start.ToString("ddd M/d", CultureInfo.InvariantCulture),
                    new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0), end.AddMinutes(1));
            }).ToList();

        var price = series.Max(x => x.Value) - series.Min(x => x.Value) >= 50 ? "0" : "0.00";
        var options = new CandleChartOptions
        {
            Title = title,
            Mode = CandleChartMode.Braille,
            Width = width,
            Height = height,
            VolumeHeight = 0,
            PriceFormat = price,
            ReferenceLines = baseValue is { } b ? [new ChartReferenceLine("start", b)] : [],
            TickFormat = daily ? t => t.Hour == 0 ? t.ToString("M/d", CultureInfo.InvariantCulture) : "" : null,
        };

        // Room for the axis labels, " | " and the right-hand notes. Slot sizes divide 12 hours (or are
        // whole half-days), so columns start on the hour and at midnight, where the axis puts its ticks.
        var totalMinutes = sessions.Sum(s => (s.End - s.Start).TotalMinutes);
        var plot = Math.Max(20, width - 20 - sessions.Count);
        var needed = totalMinutes / (plot * 2);
        var candidates = SlotMinutes.Where(m => m >= needed).ToList();
        for (int i = 0; ; i++)
        {
            var bucket = TimeSpan.FromMinutes(candidates[i]);
            try
            {
                return AsciiCandleChart.Render(Resample(series, sessions, bucket), sessions, options with { BucketSize = bucket });
            }
            catch (ArgumentException) when (i + 1 < candidates.Count)
            {
            }
        }
    }

    /// <summary>Divisors of 720 minutes, then whole multiples of it (up to decades across a narrow chart).</summary>
    private static readonly int[] SlotMinutes =
    [
        .. Enumerable.Range(1, 720).Where(m => 720 % m == 0),
        .. Enumerable.Range(2, 400).Select(n => n * 720),
    ];

    /// <summary>One bar per slot inside each session, interpolated between the real points, spanning from the previous slot's value.</summary>
    internal static List<OhlcBar> Resample(IReadOnlyList<(DateTime Et, double Value)> series, IReadOnlyList<ChartSession> sessions, TimeSpan bucket)
    {
        var bars = new List<OhlcBar>();
        foreach (var s in sessions)
        {
            var inside = series.Where(x => x.Et >= s.Start && x.Et < s.End).ToList();
            if (inside.Count == 0) continue;
            double? previous = null;
            for (var t = s.Start; t < s.End; t += bucket)
            {
                var slotEnd = t + bucket;
                // The slot's value: its last real point, else the line between the points around it.
                var here = inside.LastOrDefault(x => x.Et >= t && x.Et < slotEnd);
                double value;
                if (here != default) value = here.Value;
                else
                {
                    var before = inside.LastOrDefault(x => x.Et < t);
                    var after = inside.FirstOrDefault(x => x.Et >= slotEnd);
                    if (before == default || after == default) continue;
                    var f = (t - before.Et) / (after.Et - before.Et);
                    value = before.Value + (after.Value - before.Value) * f;
                }
                var from = previous ?? value;
                bars.Add(new OhlcBar(t, from, Math.Max(from, value), Math.Min(from, value), value));
                previous = value;
            }
        }
        return bars;
    }
}
