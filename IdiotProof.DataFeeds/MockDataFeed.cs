using System.Runtime.CompilerServices;
using IdiotProof.Models;

namespace IdiotProof.DataFeeds;

/// <summary>
/// Synthetic data feed for sandbox/demo mode — no API keys required.
///
/// The price series is a DETERMINISTIC function of (symbol, instant), so the
/// candle history, the latest price, and the previous close always agree —
/// the old implementation reseeded per call and they diverged.
///
/// Gapper simulation: symbols starting with "GAP" gap up every premarket;
/// other symbols gap on a seeded ~20% of days. A gap day plays the classic
/// arc — previous close → gapped open at 4:00 ET → momentum run through the
/// premarket → rollover in the last ~15 minutes before the 9:30 bell — so the
/// entire gapper lifecycle (screen → entry → hold → peak-giveback sell-off)
/// can be exercised end-to-end against the Sandbox broker with zero keys.
/// </summary>
public sealed class MockDataFeed : IMarketDataFeed
{
    public string FeedName => "Mock";

    private static readonly TimeZoneInfo Eastern = ResolveEastern();

    private static TimeZoneInfo ResolveEastern()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
    }

    public async IAsyncEnumerable<Candle> GetHistoricalCandlesAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan candleSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        if (candleSize <= TimeSpan.Zero) candleSize = TimeSpan.FromMinutes(1);

        // Daily bars: flat closes at the symbol's base price — a stable
        // previous close for gap math.
        if (candleSize >= TimeSpan.FromDays(1))
        {
            var basePx = BasePrice(symbol);
            var day = startUtc.Date;
            while (day < endUtc && !ct.IsCancellationRequested)
            {
                if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                {
                    yield return new Candle
                    {
                        Symbol = symbol,
                        StartUtc = day, EndUtc = day.AddDays(1),
                        Open = basePx, High = basePx * 1.01m, Low = basePx * 0.99m, Close = basePx,
                        Volume = 5_000_000,
                        Note = "Mock-daily",
                    };
                }
                day = day.AddDays(1);
            }
            yield break;
        }

        var current = startUtc;
        while (current < endUtc && !ct.IsCancellationRequested)
        {
            var candleEnd = current.Add(candleSize);

            // No weekend minute bars — the daily branch above already skips
            // Saturday/Sunday, but this intraday branch happily synthesized
            // them, so a replay pointed at a weekend date reported phantom
            // trades on bars no market ever printed.
            var barEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(current, DateTimeKind.Utc), Eastern);
            if (barEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                current = candleEnd;
                continue;
            }

            var open  = PriceAt(symbol, current);
            var close = PriceAt(symbol, candleEnd);
            var high  = Math.Round(Math.Max(open, close) * 1.002m, 4);
            var low   = Math.Max(Math.Round(Math.Min(open, close) * 0.998m, 4), 0.01m);

            yield return new Candle
            {
                Symbol   = symbol,
                Open     = open,
                High     = high,
                Low      = low,
                Close    = close,
                Volume   = VolumeAt(symbol, current),
                StartUtc = current,
                EndUtc   = candleEnd,
                Note     = "Mock",
            };
            current = candleEnd;
        }
    }

    public Task<LatestPrice?> GetLatestPriceAsync(string symbol, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return Task.FromResult<LatestPrice?>(new LatestPrice(symbol, PriceAt(symbol, now), now, FeedName));
    }

    // ── Deterministic price path ────────────────────────────────────────

    private static decimal BasePrice(string symbol)
    {
        var seed = StableHash(symbol);
        return symbol.ToUpperInvariant() switch
        {
            "TSLA" => 220m,
            "AAPL" => 185m,
            "NVDA" => 480m,
            "SPY"  => 520m,
            "QQQ"  => 440m,
            "MSFT" => 415m,
            _      => 100m + (seed % 400)
        };
    }

    /// <summary>Gap fraction for the symbol's ET day, 0 when it isn't a gap day.</summary>
    private static double GapFraction(string symbol, DateOnly dayEt)
    {
        if (symbol.StartsWith("GAP", StringComparison.OrdinalIgnoreCase))
            return 0.08; // demo symbols always gap +8%

        var daySeed = (int)(StableHash(symbol) ^ (uint)dayEt.DayNumber);
        var roll = new Random(daySeed).NextDouble();
        return roll < 0.20 ? 0.03 + roll * 0.5 : 0.0; // ~20% of days gap +3–13%
    }

    /// <summary>
    /// The price at any instant. Gap days follow: previous close overnight →
    /// gapped level at 4:00 ET → momentum climb (up to +6% over the gap) that
    /// peaks ~9:10 ET → rollover into the bell → slow fade through RTH.
    /// Non-gap days meander ±1% around base with deterministic noise.
    /// </summary>
    private static decimal PriceAt(string symbol, DateTime utc)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eastern);
        var dayEt = DateOnly.FromDateTime(et);
        var basePx = (double)BasePrice(symbol);
        var gap = GapFraction(symbol, dayEt);

        // Deterministic per-minute noise: ±0.15%.
        var minuteOfDay = (int)et.TimeOfDay.TotalMinutes;
        var noiseSeed = (int)(StableHash(symbol) ^ (uint)(dayEt.DayNumber * 1441 + minuteOfDay));
        var noise = (new Random(noiseSeed).NextDouble() - 0.5) * 0.003;

        double level;
        var t = et.TimeOfDay;
        if (gap <= 0)
        {
            level = basePx * (1 + noise * 3);
        }
        else if (t < new TimeSpan(4, 0, 0))
        {
            level = basePx; // overnight: previous close
        }
        else if (t < new TimeSpan(9, 10, 0))
        {
            // Momentum run: gapped open climbing toward the peak at 9:10.
            var progress = (t - new TimeSpan(4, 0, 0)) / (new TimeSpan(9, 10, 0) - new TimeSpan(4, 0, 0));
            level = basePx * (1 + gap) * (1 + 0.06 * progress);
        }
        else if (t < new TimeSpan(9, 30, 0))
        {
            // Rollover into the bell: give back ~40% of the run by 9:30 —
            // enough to trip a 20–35% peak-giveback exit.
            var fade = (t - new TimeSpan(9, 10, 0)) / (new TimeSpan(9, 30, 0) - new TimeSpan(9, 10, 0));
            level = basePx * (1 + gap) * (1.06 - 0.024 * fade);
        }
        else
        {
            // RTH: slow fade off the premarket levels.
            level = basePx * (1 + gap) * 1.036 * (1 - 0.01 * Math.Min(1, (t - new TimeSpan(9, 30, 0)) / TimeSpan.FromHours(6)));
        }

        return Math.Max(0.01m, Math.Round((decimal)(level * (1 + noise)), 4));
    }

    private static decimal VolumeAt(string symbol, DateTime utc)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Eastern);
        var gap = GapFraction(symbol, DateOnly.FromDateTime(et));
        var tod = et.TimeOfDay;
        var premarket = tod >= new TimeSpan(4, 0, 0) && tod < new TimeSpan(9, 30, 0);
        var nearOpenOrClose = (tod >= new TimeSpan(9, 30, 0) && tod < new TimeSpan(10, 30, 0))
                           || (tod >= new TimeSpan(15, 0, 0) && tod < new TimeSpan(16, 0, 0));

        // Gap-day premarket volume comes in BURSTS (every third minute spikes
        // 8×) so the rolling VolumeRatio the IsVolumeAbove condition computes
        // actually exceeds 2× on spike bars — a flat multiplier would keep the
        // ratio pinned near 1.0 and the mock gapper could never fire.
        var multiplier = gap > 0 && premarket
            ? ((int)tod.TotalMinutes % 3 == 0 ? 8.0 : 1.5)
            : nearOpenOrClose ? 3.0 : 1.0;
        var seed = (int)(StableHash(symbol) ^ (uint)(int)tod.TotalMinutes);
        return (decimal)(new Random(seed).Next(50_000, 500_000) * multiplier);
    }

    /// <summary>Culture/process-stable hash (string.GetHashCode is randomized per process).</summary>
    private static uint StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s.ToUpperInvariant()) h = (h ^ c) * 16777619;
            return h & 0x7FFFFFFF;
        }
    }
}
