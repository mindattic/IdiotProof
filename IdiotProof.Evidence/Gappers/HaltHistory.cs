using System.Globalization;
using System.Xml.Linq;

namespace IdiotProof.Evidence.Gappers;

/// <summary>One trading halt or LULD pause from the Nasdaq Trader halt history (all US listing markets).</summary>
public sealed record TradeHalt(string Symbol, string Market, string Reason, DateTime HaltUtc, DateTime? ResumeQuoteUtc, DateTime? ResumeTradeUtc)
{
    /// <summary>A limit-up/limit-down or volatility pause (LUDP, M).</summary>
    public bool IsVolatilityPause => Reason is "LUDP" or "M";

    /// <summary>A news halt: T1 news pending, T2 news released, T3 news and resumption times (T1/T12 combos included).</summary>
    public bool IsNews => Reason.StartsWith("T1", StringComparison.Ordinal) || Reason is "T2" or "T3";
}

/// <summary>
/// The Nasdaq Trader halt history (avenues.md A1, IP-T457): one RSS file per day from
/// <c>https://www.nasdaqtrader.com/rss.aspx?feed=tradehalts&amp;haltdate=MMDDYYYY</c>, every halt to the
/// second in ET. Indexed by symbol for point-in-time queries: a halt is known from its halt time.
/// </summary>
public sealed class HaltHistory
{
    private static readonly XNamespace Ndaq = "http://www.nasdaqtrader.com/";
    private readonly Dictionary<string, List<TradeHalt>> bySymbol;

    public HaltHistory(IEnumerable<TradeHalt> halts)
    {
        bySymbol = halts.GroupBy(h => h.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.HaltUtc).ToList(), StringComparer.OrdinalIgnoreCase);
        Count = bySymbol.Values.Sum(l => l.Count);
    }

    public int Count { get; }

    /// <summary>Parses one day's RSS document; rows without a parseable halt time are skipped.</summary>
    public static IEnumerable<TradeHalt> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        foreach (var item in doc.Descendants("item"))
        {
            string V(string name) => item.Element(Ndaq + name)?.Value.Trim() ?? "";
            var symbol = V("IssueSymbol");
            var halt = Et(V("HaltDate"), V("HaltTime"));
            if (symbol.Length == 0 || halt is null) continue;
            yield return new TradeHalt(symbol, V("Mkt"), V("ReasonCode"), halt.Value, Et(V("ResumptionDate"), V("ResumptionQuoteTime")), Et(V("ResumptionDate"), V("ResumptionTradeTime")));
        }
    }

    /// <summary>Loads every *.xml day file in a directory; null when the directory does not exist.</summary>
    public static HaltHistory? Load(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        var all = new List<TradeHalt>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
        {
            try { all.AddRange(Parse(File.ReadAllText(file))); }
            catch (System.Xml.XmlException) { }
        }
        // The same halt can appear on two days' feeds (halt one day, resume the next).
        return new HaltHistory(all.DistinctBy(h => (h.Symbol.ToUpperInvariant(), h.HaltUtc, h.Reason)));
    }

    /// <summary>Halts of the symbol with HaltUtc in [fromUtc, toUtc).</summary>
    public IEnumerable<TradeHalt> Between(string symbol, DateTime fromUtc, DateTime toUtc) =>
        bySymbol.TryGetValue(symbol, out var list) ? list.Where(h => h.HaltUtc >= fromUtc && h.HaltUtc < toUtc) : [];

    private static DateTime? Et(string date, string time)
    {
        if (!DateOnly.TryParseExact(date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
        if (!TimeSpan.TryParseExact(time, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var t)) return null;
        return MinuteBarMath.ToUtc(d, t);
    }
}

/// <summary>Point-in-time halt features for one symbol at decision time t on day D (only halts with HaltUtc &lt;= t).</summary>
public static class HaltFeatures
{
    public const string PriorDayPauses = "x.haltPausesD1";
    public const string NewsHaltSinceClose = "x.haltNewsSinceClose";
    public const string PausesToday = "x.haltPausesToday";
    public const string MinutesSinceLastHalt = "x.haltMinutesSinceLast";

    public static IEnumerable<KeyValuePair<string, double>> Compute(HaltHistory h, string symbol, DateOnly dateEt, DateTime decisionUtc, DateOnly priorTradingDay)
    {
        var priorOpen = MinuteBarMath.ToUtc(priorTradingDay, new TimeSpan(4, 0, 0));
        var priorClose = MinuteBarMath.ToUtc(priorTradingDay, new TimeSpan(16, 0, 0));
        var dayStart = MinuteBarMath.ToUtc(dateEt, TimeSpan.Zero);
        var known = h.Between(symbol, priorOpen, decisionUtc.AddTicks(1)).ToList();
        yield return new(PriorDayPauses, known.Count(x => x.IsVolatilityPause && x.HaltUtc < priorClose.AddHours(4)));
        yield return new(NewsHaltSinceClose, known.Any(x => x.IsNews && x.HaltUtc >= priorClose) ? 1 : 0);
        yield return new(PausesToday, known.Count(x => x.IsVolatilityPause && x.HaltUtc >= dayStart));
        var last = known.LastOrDefault(x => x.HaltUtc >= priorClose);
        yield return new(MinutesSinceLastHalt, last is null ? double.NaN : (decisionUtc - last.HaltUtc).TotalMinutes);
    }

    /// <summary>The first volatility pause in [09:30, 09:50) ET on D (the band-halving window), as minutes after midnight ET; NaN if none. An outcome, never a feature.</summary>
    public static double FirstOpenWindowPauseMinEt(HaltHistory h, string symbol, DateOnly dateEt)
    {
        var from = MinuteBarMath.ToUtc(dateEt, new TimeSpan(9, 30, 0));
        var to = MinuteBarMath.ToUtc(dateEt, new TimeSpan(9, 50, 0));
        var first = h.Between(symbol, from, to).FirstOrDefault(x => x.IsVolatilityPause);
        if (first is null) return double.NaN;
        var et = MinuteBarMath.ToEastern(first.HaltUtc);
        return et.Hour * 60 + et.Minute + et.Second / 60.0;
    }
}
