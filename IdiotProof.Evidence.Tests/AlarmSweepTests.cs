using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>
/// The shared alarm sweep (IP-T475) on a tiny synthetic morning: the rank-k pass logic that
/// <c>explore alarms</c> and amendment 04's H25/H26 both call (gap-cap rows are not ranked, a ranked
/// row that fails a floor still takes its slot, a sample row's weight fills the k slots), the
/// gainers + news ordering, and what an alarm means (hit with lead, false alarm, neither).
/// </summary>
public class AlarmSweepTests
{
    private static readonly DateOnly Day = new(2026, 7, 17);

    private static AlarmMorning Morning(string symbol, double weight, float[] pmRet, float[] dollar, float[]? conf = null, bool[]? news = null,
        bool reach20 = false, int first20 = int.MaxValue) => new()
    {
        Symbol = symbol, Day = Day, Weight = weight, Sampled = weight > 1, Reach20 = reach20, First20 = first20, First10 = int.MaxValue,
        SVol = new float[]?[AlarmSweep.Drifts.Length], STr = new float[]?[AlarmSweep.Drifts.Length],
        MaxSVol = new float[AlarmSweep.Drifts.Length], MaxSTr = new float[AlarmSweep.Drifts.Length],
        PmRet = pmRet, CumDollar = dollar, Conf = conf ?? pmRet.Select(_ => 1f).ToArray(), VolRatio = pmRet.Select(_ => float.NaN).ToArray(), News = news,
    };

    [Test]
    public void RankK_SkipsCappedRows_FailedRowsKeepTheirSlot_AndAlarmsOncePerMorning()
    {
        var rows = new List<AlarmMorning>
        {
            Morning("A", 1, [0.20f, 0.20f, 0.20f], [1e6f, 1e6f, 1e6f]),                           // above the 15% cap: never ranked
            Morning("B", 1, [0.03f, 0.06f, 0.06f], [1e5f, 1e5f, 1e5f], reach20: true, first20: 2), // gap below 5% at minute 0, passes at 1
            Morning("C", 1, [0.08f, 0.08f, 0.08f], [1e4f, 1e5f, 1e5f]),                           // under $50k at minute 0, passes at 1
            Morning("D", 1, [0.10f, 0.10f, 0.10f], [1e6f, 1e6f, 1e6f], reach20: true, first20: -3), // 4th: outside top 2 until B and C alarm
        };
        int[] order = [0, 1, 2, 3];
        var sorted = new[] { order, order, order };
        var alarm = AlarmSweep.RankAlarms(rows, sorted, new AlarmRankRule(AlarmScoreKind.Composite, 2, 0.05, 0.15, NeedConf: true));

        Assert.Multiple(() =>
        {
            // Minute 0: B and C are ranked (taking both slots) but fail their floors. Minute 1: both pass.
            // Minute 2: B and C are already alarmed, so D moves into the top 2 and alarms.
            Assert.That(alarm, Is.EqualTo(new[] { -1, 1, 1, 2 }));
            var b = AlarmSweep.Outcome(rows[1], alarm[1]);
            Assert.That(b.Hit, Is.True);
            Assert.That(b.Lead20, Is.EqualTo(1));
            Assert.That(AlarmSweep.Outcome(rows[2], alarm[2]).False, Is.True);
            var d = AlarmSweep.Outcome(rows[3], alarm[3]);
            Assert.That(d.Alarmed && !d.Hit && !d.False, Is.True, "formed before 06:00: an alarm, neither hit nor false");
            Assert.That(rows[3].Scorable, Is.False);
        });

        var totals = new AlarmDayTotals(Day, rows.Where(r => r.Scorable).Sum(r => r.Weight), rows.Where(r => r.Forming).Sum(r => r.Weight), rows.Sum(r => r.Weight),
            [3, 0], [1, 0], [1, 0]);
        Assert.Multiple(() =>
        {
            Assert.That(AlarmComparison.HitRate([totals], 0), Is.EqualTo(1.0));
            Assert.That(AlarmComparison.FaPer100([totals], 0), Is.EqualTo(100.0 / 3).Within(1e-9));
            Assert.That(AlarmComparison.FaPer100([totals], 0, allRows: true), Is.EqualTo(25.0).Within(1e-9));
        });
    }

    [Test]
    public void GainersNews_FreshNewsRanksFirst_AndASampleRowFillsTheSlots()
    {
        var rows = new List<AlarmMorning>
        {
            Morning("NEWS", 1, [0.03f], [1e5f], news: [true]),   // smallest gap, but fresh news ranks it first
            Morning("SAMPLE", 10, [0.09f], [1e5f], news: [false]), // weight 10 takes every remaining slot of k = 10
            Morning("BIG", 1, [0.08f], [1e5f], news: [false]),
        };
        var scores = AlarmSweep.Scores(rows);
        Assert.That(scores.Sorted(AlarmScoreKind.GainersNews)[0], Is.EqualTo(new[] { 0, 1, 2 }));

        var alarm = AlarmSweep.RankAlarms(rows, scores.Sorted(AlarmScoreKind.GainersNews), new AlarmRankRule(AlarmScoreKind.GainersNews, 10, 0.02, 0.15, NeedConf: false));
        Assert.That(alarm, Is.EqualTo(new[] { 0, 0, -1 }), "after NEWS (1) and SAMPLE (10) the weighted count is 11 >= 10, so BIG is outside the top 10");
    }
}
