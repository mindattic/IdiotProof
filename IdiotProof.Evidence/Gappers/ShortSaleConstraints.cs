using System.Globalization;

namespace IdiotProof.Evidence.Gappers;

/// <summary>One FINRA short-interest record: shares short as of a settlement date.</summary>
public sealed record ShortInterestRecord(string Symbol, DateOnly Settlement, double ShortShares, double AvgDailyVolume, double DaysToCover, double? ChangePct);

/// <summary>
/// FINRA bi-monthly short interest (avenues.md A5b, IP-T461), every exchange-listed and OTC stock, from the
/// pipe-delimited <c>shrtYYYYMMDD.csv</c> files. FINRA publishes about seven business days after the
/// settlement date, so a record is only used once its conservative publication day is strictly before D.
/// </summary>
public sealed class ShortInterestHistory
{
    private readonly Dictionary<string, List<ShortInterestRecord>> bySymbol;

    public ShortInterestHistory(IEnumerable<ShortInterestRecord> records)
    {
        bySymbol = records.GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Settlement).ToList(), StringComparer.OrdinalIgnoreCase);
        Count = bySymbol.Values.Sum(l => l.Count);
    }

    public int Count { get; }

    public static IEnumerable<ShortInterestRecord> Parse(IEnumerable<string> lines)
    {
        string[]? header = null;
        foreach (var line in lines)
        {
            var cells = line.Split('|');
            if (header is null) { header = cells; continue; }
            if (cells.Length != header.Length) continue;
            string V(string name) => Array.IndexOf(header, name) is var i and >= 0 ? cells[i].Trim() : "";
            double? D(string name) => double.TryParse(V(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : null;
            if (!DateOnly.TryParseExact(V("settlementDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var settle)) continue;
            if (V("symbolCode") is not { Length: > 0 } symbol || D("currentShortPositionQuantity") is not { } shortShares) continue;
            yield return new ShortInterestRecord(symbol, settle, shortShares, D("averageDailyVolumeQuantity") ?? double.NaN, D("daysToCoverQuantity") ?? double.NaN, D("changePercent"));
        }
    }

    public static ShortInterestHistory? Load(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        var all = new List<ShortInterestRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "shrt*.csv")) all.AddRange(Parse(File.ReadLines(file)));
        return new ShortInterestHistory(all.DistinctBy(r => (r.Symbol.ToUpperInvariant(), r.Settlement)));
    }

    /// <summary>The latest record whose publication day (from <paramref name="published"/>) is strictly before <paramref name="dateEt"/>.</summary>
    public ShortInterestRecord? KnownBefore(string symbol, DateOnly dateEt, Func<DateOnly, DateOnly> published)
    {
        if (!bySymbol.TryGetValue(symbol, out var list)) return null;
        for (int i = list.Count - 1; i >= 0; i--)
            if (published(list[i].Settlement) < dateEt) return list[i];
        return null;
    }
}

/// <summary>
/// SEC Rule 201 (avenues.md A5a): a drop of 10% or more below the prior close triggers the short-sale
/// price test for the rest of that day and all of the next, so whether it binds on D is known from D-1's
/// daily bar before 04:00 on D.
/// </summary>
public static class ShortSaleRestriction
{
    public const double TriggerDrop = -0.10;

    /// <summary>D-1's low against D-2's close, minus 1; null without both.</summary>
    public static double? PriorDayLowVsClose(decimal? d1Low, decimal? d2Close) =>
        d1Low is { } low && d2Close is > 0 and { } close ? (double)(low / close - 1m) : null;

    public static bool ActiveOn(double? priorDayLowVsClose) => priorDayLowVsClose is { } x && x <= TriggerDrop + 1e-12;
}

/// <summary>Short-sale constraint features at day D (all known before 04:00 ET on D).</summary>
public static class ShortConstraintFeatures
{
    public const string SsrActive = "x.ssrActive";
    public const string D1LowVsClose = "x.ssrD1LowVsClose";
    public const string DaysToCover = "x.siDaysToCover";
    public const string ChangePct = "x.siChangePct";
    public const string AgeDays = "x.siAgeDays";
}
