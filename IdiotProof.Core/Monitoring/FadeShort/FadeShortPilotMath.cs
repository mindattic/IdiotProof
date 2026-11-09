using System.Globalization;
using System.Text.Json;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Core.Monitoring.FadeShort;

/// <summary>The fade-short signal at 09:45 ET (runs/explore/fade-short, train): the inputs and whether all three conditions held.</summary>
public sealed record FadeShortPilotSignal(
    double PmHighPct, double GapPct, double ExtensionPct, decimal PmHigh, decimal Open0930, decimal Close, bool PmHighAtLeast100, bool Fired);

/// <summary>What <c>GET /v2/assets/{symbol}</c> said about shorting one symbol.</summary>
public sealed record AssetBorrowState(bool? Shortable, bool? EasyToBorrow, bool? Marginable, string? Status, string? BorrowStatus);

/// <summary>One advisory locate quote (<c>GET /v1/locates/quotes</c>): shares available and the fee per share, or Alpaca's error code.</summary>
public sealed record LocateQuote(string Symbol, double? AvailableQty, decimal? FeePerShare, string? ErrorCode, string? ErrorMessage)
{
    public bool? Available => ErrorCode is null ? AvailableQty is > 0 : null;
}

/// <summary>
/// Pure pieces of the fade-short borrow pilot (IP-T480): the signal thresholds and their evaluation on
/// minute bars (through <see cref="GapperShortSimulator.Signal"/>, the same inputs the explore run used),
/// Rule 201 from daily bars, the Reg SHO D-1 lookup, and the parsers for Alpaca's asset and locate-quote JSON.
/// </summary>
public static class FadeShortPilotMath
{
    /// <summary>Premarket high at or above +50% over the adjusted prior close.</summary>
    public const double MinPmHighPct = 50;
    /// <summary>The +100% sub-tier, recorded alongside.</summary>
    public const double PmHigh100Pct = 100;
    /// <summary>Still at or above +20% over the prior close at 09:45.</summary>
    public const double MinGapAtDecisionPct = 20;
    /// <summary>Extension from the 09:30 open below 10% at 09:45.</summary>
    public const double MaxExtensionPct = 10;

    public static readonly TimeSpan DecisionEt = new(9, 45, 0);

    public static DateTime DecisionUtc(DateOnly dateEt) => MinuteBarMath.ToUtc(dateEt, DecisionEt);

    /// <summary>The 09:45 signal from bars ending by 09:45 ET; null without a premarket bar, a 09:30 bar or a prior close.</summary>
    public static FadeShortPilotSignal? Evaluate(IReadOnlyList<Candle> dayBars, DateOnly dateEt, decimal prevClose) =>
        EvaluateAt(dayBars, dateEt, prevClose, DecisionUtc(dateEt));

    public static FadeShortPilotSignal? EvaluateAt(IReadOnlyList<Candle> dayBars, DateOnly dateEt, decimal prevClose, DateTime decisionUtc)
    {
        var s = GapperShortSimulator.Signal(dayBars, dateEt, prevClose, decisionUtc);
        if (s is null) return null;
        bool fired = s.PmHighGapPct >= MinPmHighPct && s.GapPct >= MinGapAtDecisionPct && s.ExtensionPct < MaxExtensionPct;
        return new FadeShortPilotSignal(s.PmHighGapPct, s.GapPct, s.ExtensionPct, s.PmHigh, s.Open0930, s.Close, s.PmHighGapPct >= PmHigh100Pct, fired);
    }

    /// <summary>Premarket high (bars starting 04:00-09:29 ET and ending by <paramref name="asOfUtc"/>) over the prior close, %; null without one.</summary>
    public static (decimal PmHigh, double Pct)? PremarketHigh(IReadOnlyList<Candle> dayBars, DateOnly dateEt, decimal prevClose, DateTime asOfUtc)
    {
        if (prevClose <= 0) return null;
        var start = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.SessionStartEt);
        var open = MinuteBarMath.ToUtc(dateEt, MinuteBarMath.RthOpenEt);
        decimal high = 0;
        foreach (var b in dayBars)
            if (b.Volume > 0 && b.Open > 0 && b.StartUtc >= start && b.StartUtc < open && b.EndUtc <= asOfUtc) high = Math.Max(high, b.High);
        return high > 0 ? (high, (double)(high / prevClose - 1m) * 100) : null;
    }

    /// <summary>
    /// The adjusted prior close (D-1), and Rule 201 for D (D-1 low over D-2 close, minus 1), from daily bars stamped
    /// before <paramref name="dateEt"/>. Null fields when there are not enough days.
    /// </summary>
    public static (decimal? PrevClose, double? D1LowVsD2Close, bool? SsrActive) PriorDays(IReadOnlyList<Candle> dailyBars, DateOnly dateEt)
    {
        var prior = dailyBars.Where(b => DateOnly.FromDateTime(MinuteBarMath.ToEastern(b.StartUtc)) < dateEt).OrderBy(b => b.StartUtc).ToList();
        if (prior.Count == 0) return (null, null, null);
        var d1 = prior[^1];
        if (prior.Count < 2) return (d1.Close, null, null);
        var x = ShortSaleRestriction.PriorDayLowVsClose(d1.Low, prior[^2].Close);
        return (d1.Close, x, x is null ? null : ShortSaleRestriction.ActiveOn(x));
    }

    /// <summary>The last trading day before <paramref name="dateEt"/> (weekends and market holidays skipped).</summary>
    public static DateOnly PriorTradingDay(DateOnly dateEt)
    {
        var d = dateEt.AddDays(-1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || MarketTime.IsMarketHoliday(d)) d = d.AddDays(-1);
        return d;
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is on any D-1 Reg SHO threshold list in <paramref name="dir"/> (files named
    /// *YYYYMMDD.txt, IP-T469 format); null when the directory holds no valid list for that day.
    /// </summary>
    public static bool? RegShoListedD1(string? dir, string symbol, DateOnly dateEt)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
        var stamp = PriorTradingDay(dateEt).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        bool any = false;
        foreach (var file in Directory.EnumerateFiles(dir, $"*{stamp}.txt"))
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length == 0 || !RegShoThresholdHistory.LooksValid(lines[0])) continue;
            any = true;
            if (RegShoThresholdHistory.Parse(lines).Contains(symbol, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return any ? false : null;
    }

    public static AssetBorrowState ParseAsset(JsonElement asset) => new(
        Bool(asset, "shortable"), Bool(asset, "easy_to_borrow"), Bool(asset, "marginable"), Str(asset, "status"), Str(asset, "borrow_status"));

    /// <summary>Parses <c>{"quotes":[{symbol, available_qty, price, quoted_at}], "errors":[{symbol, code, message}]}</c>.</summary>
    public static IReadOnlyDictionary<string, LocateQuote> ParseLocateQuotes(JsonElement root)
    {
        var map = new Dictionary<string, LocateQuote>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object) return map;
        if (root.TryGetProperty("quotes", out var quotes) && quotes.ValueKind == JsonValueKind.Array)
            foreach (var q in quotes.EnumerateArray())
                if (Str(q, "symbol") is { Length: > 0 } sym)
                    map[sym] = new LocateQuote(sym, Num(q, "available_qty"), Num(q, "price") is { } p ? (decimal)p : null, null, null);
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            foreach (var e in errors.EnumerateArray())
                if (Str(e, "symbol") is { Length: > 0 } sym && !map.ContainsKey(sym))
                    map[sym] = new LocateQuote(sym, null, null, Str(e, "code") ?? "error", Str(e, "message"));
        return map;
    }

    /// <summary>Alpaca bars (<c>t, o, h, l, c, v</c>) as candles of <paramref name="span"/>.</summary>
    public static Candle? ParseBar(string symbol, JsonElement b, TimeSpan span)
    {
        if (Str(b, "t") is not { } t || !DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start)) return null;
        if (Num(b, "o") is not { } o || Num(b, "h") is not { } h || Num(b, "l") is not { } l || Num(b, "c") is not { } c) return null;
        return new Candle
        {
            Symbol = symbol, StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc), EndUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc) + span,
            Open = (decimal)o, High = (decimal)h, Low = (decimal)l, Close = (decimal)c, Volume = (decimal)(Num(b, "v") ?? 0),
        };
    }

    public static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;

    public static double? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}
