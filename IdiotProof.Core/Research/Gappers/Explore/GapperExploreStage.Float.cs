using System.Globalization;
using System.IO.Compression;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>The SEC companyfacts.zip folder (IP-T459); GAPPER_COMPANYFACTS_DIR overrides, default D:\IdiotProofData\sec-companyfacts.</summary>
    public string CompanyFactsDir() => Environment.GetEnvironmentVariable("GAPPER_COMPANYFACTS_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(HaltsDir().TrimEnd('\\', '/'))!, "sec-companyfacts");

    private string SharesCsv() => Path.Combine(CompanyFactsDir(), "shares-outstanding.csv");
    private string CikMapCsv() => Path.Combine(CacheDir(), "cikmap.csv");

    private SharesOutstandingHistory? shares;
    private Dictionary<string, int>? cikMap;

    /// <summary>
    /// `explore float-import`: streams companyfacts.zip once into shares-outstanding.csv (cik, period end,
    /// filing date, shares, concept) and writes the study's symbol-to-CIK map beside the panel cache.
    /// </summary>
    private async Task<(List<string>, int, string?)> FloatImportAsync(CancellationToken ct)
    {
        var zip = Path.Combine(CompanyFactsDir(), "companyfacts.zip");
        if (!File.Exists(zip)) return ([$"{zip} is missing; download https://www.sec.gov/Archives/edgar/daily-index/xbrl/companyfacts.zip there first"], 0, null);
        var facts = new List<SharesOutstandingFact>();
        int entries = 0, companies = 0;
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                entries++;
                await using var stream = entry.Open();
                List<SharesOutstandingFact> parsed;
                try { parsed = SharesOutstandingHistory.Parse(stream); }
                catch (System.Text.Json.JsonException) { continue; }
                if (parsed.Count > 0) { companies++; facts.AddRange(parsed); }
                if (entries % 2000 == 0) s.Logger.LogInformation("[{Now}] explore float-import: {Entries} filers read, {Companies} with shares outstanding", GapperStudyServices.Et(s.UtcNow), entries, companies);
            }
        }
        var history = new SharesOutstandingHistory(facts);
        history.WriteCsv(SharesCsv());

        await using var db = await s.Db.CreateDbContextAsync(ct);
        var map = await db.GapperStudyCikMap.AsNoTracking().Where(m => m.Cik != null).Select(m => new { m.Symbol, m.Cik }).ToListAsync(ct);
        Directory.CreateDirectory(CacheDir());
        File.WriteAllLines(CikMapCsv(), ["symbol,cik", .. map.Select(m => $"{m.Symbol},{m.Cik}")]);
        return ([$"{entries:N0} filers read, {companies:N0} with shares outstanding, {history.Count:N0} facts -> {SharesCsv()}; {map.Count:N0} symbols mapped to a CIK -> {CikMapCsv()}"], 0, SharesCsv());
    }

    /// <summary>Adds the float-proxy block when both the shares file and the CIK map exist.</summary>
    private ExploreTable AddFloat(ExploreTable t)
    {
        if (t.Feature(FloatFeatures.LogShares) >= 0) return t;
        shares ??= SharesOutstandingHistory.ReadCsv(SharesCsv());
        if (cikMap is null && File.Exists(CikMapCsv()))
        {
            cikMap = new(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(CikMapCsv()).Skip(1))
                if (line.Split(',') is [var sym, var c] && int.TryParse(c, CultureInfo.InvariantCulture, out var cik)) cikMap[sym] = cik;
        }
        if (shares is null || cikMap is null) return t;
        int logVol = t.Feature("LogPmCumVol");
        return ExploreDerived.AddColumns(t, [FloatFeatures.LogShares, FloatFeatures.Rotation, FloatFeatures.AgeDays, FloatFeatures.GrowthYear], r =>
        {
            if (!cikMap.TryGetValue(t.Symbols[r], out var cik) || shares.KnownBefore(cik, t.Days[r]) is not { } f) return [double.NaN, double.NaN, double.NaN, double.NaN];
            double pmVol = logVol >= 0 && t.Value(r, logVol) is var lv && !float.IsNaN(lv) ? Math.Exp(lv) - 1 : double.NaN;
            return [Math.Log(f.Shares), pmVol / f.Shares, t.Days[r].DayNumber - f.Filed.DayNumber, shares.GrowthOverYear(cik, t.Days[r]) ?? double.NaN];
        });
    }
}
