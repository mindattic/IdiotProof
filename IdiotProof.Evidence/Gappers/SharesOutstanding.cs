using System.Globalization;
using System.Text.Json;

namespace IdiotProof.Evidence.Gappers;

/// <summary>One reported shares-outstanding value: as of <see cref="End"/>, public from <see cref="Filed"/>.</summary>
public sealed record SharesOutstandingFact(int Cik, DateOnly End, DateOnly Filed, double Shares, string Source);

/// <summary>
/// Shares outstanding from SEC XBRL company facts (avenues.md A3, IP-T459): dei:EntityCommonStockSharesOutstanding
/// (cover page) and, where a filer never tags it, us-gaap:CommonStockSharesOutstanding. A value is known from
/// its filing date, so a lookup for day D uses only facts filed strictly before D. Used as a float proxy:
/// the SEC data has no float, and the proxy goes stale after dilution until the next filing.
/// </summary>
public sealed class SharesOutstandingHistory
{
    private readonly Dictionary<int, List<SharesOutstandingFact>> byCik;

    public SharesOutstandingHistory(IEnumerable<SharesOutstandingFact> facts)
    {
        byCik = facts.GroupBy(f => f.Cik).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Filed).ThenBy(f => f.End).ToList());
        Count = byCik.Values.Sum(l => l.Count);
    }

    public int Count { get; }

    /// <summary>Reads one companyfacts JSON document (CIK##########.json).</summary>
    public static List<SharesOutstandingFact> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("cik", out var cikEl) || !root.TryGetProperty("facts", out var facts)) return [];
        int cik = cikEl.ValueKind == JsonValueKind.Number ? cikEl.GetInt32() : int.Parse(cikEl.GetString()!, CultureInfo.InvariantCulture);
        var result = Read(cik, facts, "dei", "EntityCommonStockSharesOutstanding");
        if (result.Count == 0) result = Read(cik, facts, "us-gaap", "CommonStockSharesOutstanding");
        return result;
    }

    private static List<SharesOutstandingFact> Read(int cik, JsonElement facts, string taxonomy, string concept)
    {
        var list = new List<SharesOutstandingFact>();
        if (!facts.TryGetProperty(taxonomy, out var tax) || !tax.TryGetProperty(concept, out var c) || !c.TryGetProperty("units", out var units) || !units.TryGetProperty("shares", out var shares))
            return list;
        foreach (var f in shares.EnumerateArray())
        {
            if (!f.TryGetProperty("val", out var val) || !f.TryGetProperty("end", out var end) || !f.TryGetProperty("filed", out var filed)) continue;
            if (!DateOnly.TryParse(end.GetString(), CultureInfo.InvariantCulture, out var e) || !DateOnly.TryParse(filed.GetString(), CultureInfo.InvariantCulture, out var fd)) continue;
            double v = val.GetDouble();
            if (v > 0) list.Add(new SharesOutstandingFact(cik, e, fd, v, $"{taxonomy}:{concept}"));
        }
        return list;
    }

    /// <summary>The latest fact (by filing, then period end) filed strictly before <paramref name="dateEt"/>.</summary>
    public SharesOutstandingFact? KnownBefore(int cik, DateOnly dateEt)
    {
        if (!byCik.TryGetValue(cik, out var list)) return null;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].Filed < dateEt) return list[i];
        return null;
    }

    /// <summary>Latest known shares over the earliest fact filed in the year before D (dilution proxy); null with fewer than two facts.</summary>
    public double? GrowthOverYear(int cik, DateOnly dateEt)
    {
        if (!byCik.TryGetValue(cik, out var list)) return null;
        var window = list.Where(f => f.Filed < dateEt && f.Filed >= dateEt.AddDays(-365)).ToList();
        return window.Count < 2 ? null : window[^1].Shares / window[0].Shares - 1;
    }

    public void WriteCsv(string file)
    {
        using var w = new StreamWriter(file);
        w.WriteLine("cik,end,filed,shares,source");
        foreach (var f in byCik.Values.SelectMany(l => l))
            w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{f.Cik},{f.End:yyyy-MM-dd},{f.Filed:yyyy-MM-dd},{f.Shares:0},{f.Source}"));
    }

    public static SharesOutstandingHistory? ReadCsv(string file)
    {
        if (!File.Exists(file)) return null;
        var facts = new List<SharesOutstandingFact>();
        foreach (var line in File.ReadLines(file).Skip(1))
        {
            var c = line.Split(',');
            if (c.Length == 5 && int.TryParse(c[0], CultureInfo.InvariantCulture, out var cik) && DateOnly.TryParse(c[1], CultureInfo.InvariantCulture, out var e)
                && DateOnly.TryParse(c[2], CultureInfo.InvariantCulture, out var fd) && double.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                facts.Add(new SharesOutstandingFact(cik, e, fd, v, c[4]));
        }
        return new SharesOutstandingHistory(facts);
    }
}

public static class FloatFeatures
{
    public const string LogShares = "x.floatLogShares";
    public const string Rotation = "x.floatRotation";
    public const string AgeDays = "x.floatAgeDays";
    public const string GrowthYear = "x.floatGrowthYear";
}
