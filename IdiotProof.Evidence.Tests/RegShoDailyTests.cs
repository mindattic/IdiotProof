using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T469: Reg SHO threshold-list and FINRA daily short-volume parsing, and point-in-time lookups (only days before D).</summary>
public class RegShoDailyTests
{
    /// <summary>Weekdays only, which is enough for these fixtures (no holidays inside them).</summary>
    private static DateOnly PrevWeekday(DateOnly d)
    {
        do d = d.AddDays(-1); while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return d;
    }

    [Test]
    public void ThresholdList_ParsesFlaggedSymbolsAndSkipsTheTimestamp()
    {
        var nasdaq = RegShoThresholdHistory.Parse([
            "Symbol|Security Name|Market Category|Reg SHO Threshold Flag|Rule 3210|Filler",
            "AEHL|ANTELOPE ENTERPRISE HLDGS LTD|S|Y|N|",
            "ATER|ATERIAN INC|S|N|N|",
            "20211001230003",
        ]).ToList();
        var nyse = RegShoThresholdHistory.Parse([
            "Symbol|Security Name|Market Category|Reg SHO Threshold Flag|Filler|Filler",
            "irnt|IronNet, Inc.|NYSE|Y||",
            "20211001210501",
        ]).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(nasdaq, Is.EqualTo(new[] { "AEHL" }));
            Assert.That(nyse, Is.EqualTo(new[] { "IRNT" }));
            Assert.That(RegShoThresholdHistory.DayOf(@"D:\x\nasdaqth20211001.txt"), Is.EqualTo(new DateOnly(2021, 10, 1)));
            Assert.That(RegShoThresholdHistory.DayOf("nyse20211001.txt"), Is.EqualTo(new DateOnly(2021, 10, 1)));
            Assert.That(RegShoThresholdHistory.LooksValid("<html><head><title>Object moved</title>"), Is.False);
        });
    }

    [Test]
    public void ThresholdList_UsesOnlyListsDatedBeforeD()
    {
        // Mon 2021-10-04 .. Fri 2021-10-08; ABC listed Tue-Thu, and also on Fri (D itself), which must not count.
        var mon = new DateOnly(2021, 10, 4);
        var listings = new[] { 1, 2, 3, 4 }.Select(i => (mon.AddDays(i), "ABC"));
        var h = new RegShoThresholdHistory(listings, Enumerable.Range(0, 5).Select(i => mon.AddDays(i)));
        Assert.Multiple(() =>
        {
            Assert.That(h.KnownBefore("ABC", mon.AddDays(4), PrevWeekday), Is.EqualTo(((bool?)true, (int?)3)), "Tue-Thu, not Fri");
            Assert.That(h.KnownBefore("abc", mon.AddDays(2), PrevWeekday), Is.EqualTo(((bool?)true, (int?)1)));
            Assert.That(h.KnownBefore("ABC", mon.AddDays(1), PrevWeekday), Is.EqualTo(((bool?)false, (int?)0)), "listed on D only");
            Assert.That(h.KnownBefore("XYZ", mon.AddDays(4), PrevWeekday), Is.EqualTo(((bool?)false, (int?)0)));
            Assert.That(h.KnownBefore("ABC", mon, PrevWeekday), Is.EqualTo(((bool?)null, (int?)null)), "no list for D-1 on disk");
        });
    }

    [Test]
    public void ShortVolume_ParsesFractionalVolumesAndSkipsBadRows()
    {
        var rows = ShortVolumeHistory.Parse([
            "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market",
            "20261106|A|238411.531288|106|578613.658485|B,Q,N",
            "20261106|AAC|10.099825|0|10570",
            "20261106|BAD|x|0|10|Q",
            "20261106|",
            "12292",
        ]).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Symbol), Is.EqualTo(new[] { "A", "AAC" }));
            Assert.That(rows[0].Date, Is.EqualTo(new DateOnly(2026, 11, 6)));
            Assert.That(rows[0].Share, Is.EqualTo(238411.531288 / 578613.658485).Within(1e-12));
            Assert.That(ShortVolumeHistory.Parse(["Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market", "20261106|A|1|0|2|Q"], new HashSet<string> { "B" }), Is.Empty);
            Assert.That(ShortVolumeHistory.LooksValid("<?xml version=\"1.0\"?><Error><Code>AccessDenied</Code>"), Is.False);
        });
    }

    [Test]
    public void ShortVolume_FeaturesUseOnlyDaysBeforeD()
    {
        // 22 weekdays ending Fri 2021-11-05 = D-1 for D = Mon 2021-11-08; share 0.40 on D-2..D-21 alternating
        // 0.35 / 0.45, 0.80 on D-1, and 0.99 on D itself (must be ignored).
        var d = new DateOnly(2021, 11, 8);
        var records = new List<ShortVolumeRecord> { new(d, "ABC", 99, 0, 100) };
        var day = d;
        for (int i = 0; i < 21; i++)
        {
            day = PrevWeekday(day);
            double share = i == 0 ? 0.80 : i % 2 == 0 ? 0.35 : 0.45;
            records.Add(new ShortVolumeRecord(day, "ABC", share * 1000, 0, 1000));
        }
        var h = new ShortVolumeHistory(records);
        var (d1, avg5, z) = h.KnownBefore("ABC", d, PrevWeekday);
        double avg = (0.80 + 0.45 + 0.35 + 0.45 + 0.35) / 5;
        double sd = Math.Sqrt(20 * 0.05 * 0.05 / 19);
        Assert.Multiple(() =>
        {
            Assert.That(d1, Is.EqualTo(0.80).Within(1e-12));
            Assert.That(avg5, Is.EqualTo(avg).Within(1e-12));
            Assert.That(z, Is.EqualTo((0.80 - 0.40) / sd).Within(1e-9));
            Assert.That(h.KnownBefore("ABC", PrevWeekday(d), PrevWeekday).ShareD1, Is.EqualTo(0.45).Within(1e-12), "D-1 of Friday is Thursday");
            Assert.That(h.KnownBefore("XYZ", d, PrevWeekday), Is.EqualTo(((double?)null, (double?)null, (double?)null)));
        });
    }
}
