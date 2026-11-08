using System.Globalization;
using IdiotProof.Core.Evidence;
using IdiotProof.Evidence.Gappers;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    /// <summary>FINRA publishes about seven business days after settlement; eight trading days keeps a margin.</summary>
    public const int ShortInterestPublishLagTradingDays = 8;

    public static DateOnly ShortInterestPublished(DateOnly settlement)
    {
        var d = settlement;
        for (int i = 0; i < ShortInterestPublishLagTradingDays; i++) d = MarketCalendar.NextTradingDay(d);
        return d;
    }

    /// <summary>The saved FINRA short-interest files (IP-T461); GAPPER_SHORT_INTEREST_DIR overrides, default D:\IdiotProofData\finra-short-interest.</summary>
    public string ShortInterestDir() => Environment.GetEnvironmentVariable("GAPPER_SHORT_INTEREST_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetDirectoryName(HaltsDir().TrimEnd('\\', '/'))!, "finra-short-interest");

    private string SsrCacheFile() => Path.Combine(CacheDir(), "ssr.csv");

    private ShortInterestHistory? shortInterest;

    /// <summary>Adds the short-sale constraint block when its sources exist: Rule 201 from the `explore ssr` cache, FINRA short interest by publication day.</summary>
    private ExploreTable AddShortSale(ExploreTable t)
    {
        if (t.Feature(ShortConstraintFeatures.DaysToCover) >= 0 || t.Feature(ShortConstraintFeatures.SsrActive) >= 0) return t;
        Dictionary<(string, DateOnly), double>? ssr = null;
        if (File.Exists(SsrCacheFile()))
        {
            ssr = [];
            foreach (var line in File.ReadLines(SsrCacheFile()).Skip(1))
            {
                var c = line.Split(',');
                if (c.Length == 3 && DateOnly.TryParse(c[1], CultureInfo.InvariantCulture, out var d) && double.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    ssr[(c[0], d)] = v;
            }
        }
        shortInterest ??= ShortInterestHistory.Load(ShortInterestDir());
        var names = new List<string>();
        if (ssr is not null) names.AddRange([ShortConstraintFeatures.SsrActive, ShortConstraintFeatures.D1LowVsClose]);
        if (shortInterest is not null) names.AddRange([ShortConstraintFeatures.DaysToCover, ShortConstraintFeatures.ChangePct, ShortConstraintFeatures.AgeDays]);
        if (names.Count == 0) return t;
        return ExploreDerived.AddColumns(t, names, r =>
        {
            var row = new List<double>();
            if (ssr is not null)
            {
                double? drop = ssr.TryGetValue((t.Symbols[r], t.Days[r]), out var v) ? v : null;
                row.Add(drop is null ? double.NaN : ShortSaleRestriction.ActiveOn(drop) ? 1 : 0);
                row.Add(drop ?? double.NaN);
            }
            if (shortInterest is not null)
            {
                var rec = shortInterest.KnownBefore(t.Symbols[r], t.Days[r], ShortInterestPublished);
                row.Add(rec?.DaysToCover ?? double.NaN);
                row.Add(rec?.ChangePct ?? double.NaN);
                row.Add(rec is null ? double.NaN : t.Days[r].DayNumber - rec.Settlement.DayNumber);
            }
            return row;
        });
    }

    /// <summary>
    /// `explore ssr`: for every symbol-day in a cached panel, D-1's low against D-2's close from the study's
    /// daily bars (bars dated before D only), written to the panel cache as ssr.csv.
    /// </summary>
    private async Task<(List<string>, int, string?)> SsrAsync(IReadOnlyList<string> times, CancellationToken ct)
    {
        var decision = times.Count == 1 ? times[0] : "08:00";
        var t = ExplorePanelFile.Read(CacheDir(), decision);
        var pairs = Enumerable.Range(0, t.Rows).Select(r => (Symbol: t.Symbols[r], Day: t.Days[r])).Distinct().GroupBy(p => p.Symbol).ToList();
        var lines = new List<string> { "symbol,date,d1LowVsD2Close" };
        int done = 0, active = 0;
        foreach (var chunk in pairs.Chunk(200))
        {
            await using var db = await s.Db.CreateDbContextAsync(ct);
            var symbols = chunk.Select(g => g.Key).ToList();
            var from = chunk.Min(g => g.Min(p => p.Day)).AddDays(-14);
            var to = chunk.Max(g => g.Max(p => p.Day));
            var bars = await db.GapperStudyDailyBars.AsNoTracking().Where(b => symbols.Contains(b.Symbol) && b.DateEt >= from && b.DateEt < to)
                .Select(b => new { b.Symbol, b.DateEt, b.Low, b.Close }).ToListAsync(ct);
            var bySymbol = bars.GroupBy(b => b.Symbol).ToDictionary(g => g.Key, g => g.OrderBy(b => b.DateEt).ToList());
            foreach (var g in chunk)
            {
                if (!bySymbol.TryGetValue(g.Key, out var list)) continue;
                foreach (var (symbol, day) in g)
                {
                    var prior = list.Where(b => b.DateEt < day).TakeLast(2).ToList();
                    if (prior.Count < 2 || prior[1].DateEt != MarketCalendar.PreviousTradingDay(day)) continue;
                    var x = ShortSaleRestriction.PriorDayLowVsClose(prior[1].Low, prior[0].Close);
                    if (x is null) continue;
                    if (ShortSaleRestriction.ActiveOn(x)) active++;
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"{symbol},{day:yyyy-MM-dd},{x.Value:0.######}"));
                }
            }
            done += chunk.Length;
            s.Logger.LogInformation("[{Now}] explore ssr: {Done}/{Total} symbols", GapperStudyServices.Et(s.UtcNow), done, pairs.Count);
        }
        File.WriteAllLines(SsrCacheFile(), lines);
        return ([$"wrote {lines.Count - 1:N0} symbol-days ({active:N0} with Rule 201 active on D) to {SsrCacheFile()}"], 0, SsrCacheFile());
    }
}
