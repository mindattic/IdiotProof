using IdiotProof.Core.Evidence;
using IdiotProof.Core.Research.Gappers;
using IdiotProof.Evidence.Gappers;
using IdiotProof.Models;

namespace IdiotProof.Core.Monitoring.Formation;

public sealed record MoveStartAlertRow(
    DateOnly DateEt, string Symbol, string M0EndEt, double? Gain, double? VolumeMultiple, bool FreshCatalyst, double? DetectionLagMinutes,
    bool? Hit, string? First20Et, double? MaxGainAfter);

public sealed record MoveStartRate(int Alerts, int Labeled, int Hits)
{
    public double? Rate => Labeled == 0 ? null : (double)Hits / Labeled;
}

public sealed record MoveStartShadowResult(
    DateOnly? From, DateOnly? To, MoveStartRate All, MoveStartRate Fresh, MoveStartRate NoFresh, IReadOnlyList<MoveStartAlertRow> Rows);

/// <summary>
/// Forward hit rates of the live move-start watch (IP-T628): every <see cref="MoveStartShadowStore.Source"/> row labeled
/// afterwards with amendment 09's label y (a regular-hours high after m0, before 16:00 ET, at least +20% over the prior
/// close), split by the fresh-catalyst flag A09-H2 tested. Today's rows stay unlabeled until 16:00 ET. Descriptive; the
/// study's numbers are train/validation figures on its own universe, so these are not a test of them.
/// </summary>
public sealed class MoveStartShadowReport(MoveStartShadowStore store, IHistoricalBarStore historical, TimeProvider time)
{
    public static readonly TimeSpan RthEndEt = new(16, 0, 0);

    public async Task<MoveStartShadowResult> RunAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var stored = await store.ReadAsync(from, to, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var rows = new List<MoveStartAlertRow>();
        foreach (var r in stored)
        {
            var extras = GapperSnapshotExtras.Parse(r.ExtraJson);
            double? gain = extras.Number(MoveStartShadowStore.GainKey);
            double? prev = extras.Number(MoveStartShadowStore.PrevCloseKey);
            double? vol = extras.Number(MoveStartShadowStore.VolumeKey), expected = extras.Number(MoveStartShadowStore.ExpectedVolumeKey);
            bool fresh = extras.Number(MoveStartShadowStore.FreshCatalystKey) is > 0;
            double? lag = extras.Number(MoveStartShadowStore.DetectionLagKey);
            var m0End = r.DecisionUtc;
            var close = MinuteBarMath.ToUtc(r.DateEt, RthEndEt);

            bool? hit = null; string? first20 = null; double? maxGain = null;
            if (prev is > 0 && now >= close)
            {
                IReadOnlyList<Candle> bars = await historical.GetBarsAsync(r.Symbol, r.DateEt, r.DateEt, BarTimeframe.Minute1, ct);
                var after = bars.Where(b => b.StartUtc >= m0End && b.EndUtc <= close).OrderBy(b => b.StartUtc).ToList();
                if (after.Count > 0)
                {
                    maxGain = after.Max(b => (double)b.High) / prev.Value - 1;
                    var touch = after.FirstOrDefault(b => (double)b.High / prev.Value - 1 >= MoveStartRule.Level);
                    hit = touch is not null;
                    if (touch is not null) first20 = MinuteBarMath.ToEastern(touch.StartUtc).ToString("HH:mm");
                }
            }
            rows.Add(new MoveStartAlertRow(r.DateEt, r.Symbol, r.DecisionTimeEt.ToString("HH:mm"), gain,
                vol is { } v && expected is > 0 ? v / expected.Value : null, fresh, lag, hit, first20, maxGain));
        }
        return new MoveStartShadowResult(
            rows.Count == 0 ? from : rows.Min(x => x.DateEt), rows.Count == 0 ? to : rows.Max(x => x.DateEt),
            Rate(rows), Rate(rows.Where(x => x.FreshCatalyst)), Rate(rows.Where(x => !x.FreshCatalyst)), rows);
    }

    internal static MoveStartRate Rate(IEnumerable<MoveStartAlertRow> rows)
    {
        var list = rows.ToList();
        return new MoveStartRate(list.Count, list.Count(x => x.Hit is not null), list.Count(x => x.Hit == true));
    }
}
