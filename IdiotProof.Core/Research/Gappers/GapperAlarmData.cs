using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// The data side of the alarm sweep (IP-T470, IP-T475), shared by <c>explore alarms</c> and amendment
/// 04's confirmatory test so the two read a morning the same way: the day's catalyst items, ADT20, and
/// a universe day turned into an <see cref="AlarmMorning"/> through <see cref="AlarmSweep.Build"/>.
/// Split-agnostic; each caller applies its own split rules.
/// </summary>
public static class GapperAlarmData
{
    /// <summary>Amendment 04's finalist R4 (the explore rule of the same name, no CUSUM gate).</summary>
    public const string R4Name = "rank composite top5, gap [5%,15%), $50k, conf>=0.5";
    public static readonly AlarmRankRule R4 = new(AlarmScoreKind.Composite, 5, 0.05, 0.15, NeedConf: true);

    /// <summary>Amendment 04's benchmark B4 (explore's matched gainers + news rule).</summary>
    public const string B4Name = "B3 gainers+news top10, gap [2%,15%), $50k";
    public static readonly AlarmRankRule B4 = new(AlarmScoreKind.GainersNews, 10, 0.02, 0.15, NeedConf: false);

    /// <summary>Catalyst items for <paramref name="symbols"/> published 02:00 ET .. the window end on <paramref name="day"/>.</summary>
    public static async Task<Dictionary<string, List<CatalystObservation>>> NewsAsync(AppDbContext db, IReadOnlyCollection<string> symbols, DateOnly day, CancellationToken ct)
    {
        var news = new Dictionary<string, List<CatalystObservation>>(StringComparer.Ordinal);
        var cFrom = MinuteBarMath.ToUtc(day, new TimeSpan(2, 0, 0));
        var cTo = MinuteBarMath.ToUtc(day, AlarmSweep.WindowEndEt);
        var items = await (from x in db.CatalystEventSymbols.AsNoTracking()
                           where symbols.Contains(x.Symbol)
                           join e in db.CatalystEvents.AsNoTracking() on x.EventId equals e.Id
                           where e.PublishedUtc >= cFrom && e.PublishedUtc <= cTo
                           select new { x.Symbol, e.PublishedUtc, e.PreClass, e.Direction, e.SourceTier, e.CatalystScore, e.StampKind }).ToListAsync(ct);
        foreach (var it in items)
        {
            if (!news.TryGetValue(it.Symbol, out var l)) news[it.Symbol] = l = [];
            l.Add(new CatalystObservation(it.PublishedUtc, it.PreClass, it.Direction, it.SourceTier, it.CatalystScore, CatalystStampKind.IsUpperBound(it.StampKind)));
        }
        return news;
    }

    /// <summary>ADT20: the mean trade count of each symbol's last 20 daily bars before <paramref name="day"/> that carry one.</summary>
    public static async Task<Dictionary<string, double>> AdtAsync(AppDbContext db, IReadOnlyCollection<string> symbols, DateOnly day, CancellationToken ct)
    {
        var from = day.AddDays(-40);
        return (await db.GapperStudyDailyBars.AsNoTracking()
                .Where(b => symbols.Contains(b.Symbol) && b.DateEt >= from && b.DateEt < day && b.TradeCount > 0)
                .Select(b => new { b.Symbol, b.DateEt, b.TradeCount }).ToListAsync(ct))
            .GroupBy(b => b.Symbol).ToDictionary(g => g.Key, g => g.OrderBy(b => b.DateEt).TakeLast(20).Average(b => (double)b.TradeCount), StringComparer.Ordinal);
    }

    public static AlarmMorning? MorningOf(GapperStudyUniverseDay u, double weight, bool sampled, IReadOnlyList<Candle> bars, double? adt, List<CatalystObservation>? news,
        TimeOfDayProfile profile, TimeOfDayProfile? tradeProfile, TimeSpan reachEt, double reachThreshold) =>
        AlarmSweep.Build(u.Symbol, u.DateEt, weight, sampled, u.AdjPrevClose, bars, u.AdvDecile, (double)u.Adv20Shares, adt, news, profile, tradeProfile, reachEt, reachThreshold);
}
