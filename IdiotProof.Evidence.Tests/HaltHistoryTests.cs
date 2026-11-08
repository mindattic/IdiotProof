using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T457: parsing the Nasdaq Trader halt feed (ET to UTC) and point-in-time halt features. 2026-07-17 is EDT.</summary>
public class HaltHistoryTests
{
    private static string Item(string sym, string reason, string date, string time, string resumeTrade = "") => $"""
        <item><title>{sym}</title>
          <ndaq:IssueSymbol>{sym}</ndaq:IssueSymbol><ndaq:Mkt>Q</ndaq:Mkt><ndaq:ReasonCode>{reason}</ndaq:ReasonCode>
          <ndaq:HaltDate>{date}</ndaq:HaltDate><ndaq:HaltTime>{time}</ndaq:HaltTime>
          <ndaq:ResumptionDate>{date}</ndaq:ResumptionDate><ndaq:ResumptionQuoteTime /><ndaq:ResumptionTradeTime>{resumeTrade}</ndaq:ResumptionTradeTime>
        </item>
        """;

    private static string Feed(params string[] items) =>
        $"""<?xml version="1.0" encoding="utf-8"?><rss version="2.0" xmlns:ndaq="http://www.nasdaqtrader.com/"><channel>{string.Concat(items)}</channel></rss>""";

    [Test]
    public void Parse_ReadsEtTimesAsUtc_AndSkipsRowsWithoutATime()
    {
        var halts = HaltHistory.Parse(Feed(Item("ABC", "LUDP", "07/17/2026", "09:31:44", "09:36:44"), Item("BAD", "T1", "07/17/2026", ""))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(halts, Has.Count.EqualTo(1));
            Assert.That(halts[0].HaltUtc, Is.EqualTo(new DateTime(2026, 7, 17, 13, 31, 44, DateTimeKind.Utc)));
            Assert.That(halts[0].ResumeTradeUtc, Is.EqualTo(new DateTime(2026, 7, 17, 13, 36, 44, DateTimeKind.Utc)));
            Assert.That(halts[0].IsVolatilityPause);
            Assert.That(halts[0].ResumeQuoteUtc, Is.Null);
        });
    }

    [Test]
    public void Features_SeeOnlyHaltsAtOrBeforeT_AndTheOpenWindowLabelIsSeparate()
    {
        var h = new HaltHistory(HaltHistory.Parse(Feed(
            Item("ABC", "LUDP", "07/16/2026", "10:05:00"),
            Item("ABC", "LUDP", "07/16/2026", "11:00:00"),
            Item("ABC", "T1", "07/17/2026", "07:10:00"),
            Item("ABC", "LUDP", "07/17/2026", "08:15:00"),
            Item("ABC", "LUDP", "07/17/2026", "09:33:30"))));
        var day = new DateOnly(2026, 7, 17);
        var f = HaltFeatures.Compute(h, "ABC", day, MinuteBarMath.ToUtc(day, new TimeSpan(8, 0, 0)), new DateOnly(2026, 7, 16)).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Multiple(() =>
        {
            Assert.That(f[HaltFeatures.PriorDayPauses], Is.EqualTo(2));
            Assert.That(f[HaltFeatures.NewsHaltSinceClose], Is.EqualTo(1), "the 07:10 news halt is before 08:00");
            Assert.That(f[HaltFeatures.PausesToday], Is.EqualTo(0), "the 08:15 pause is after t");
            Assert.That(f[HaltFeatures.MinutesSinceLastHalt], Is.EqualTo(50));
            Assert.That(HaltFeatures.FirstOpenWindowPauseMinEt(h, "ABC", day), Is.EqualTo(9 * 60 + 33.5));
            Assert.That(double.IsNaN(HaltFeatures.FirstOpenWindowPauseMinEt(h, "XYZ", day)));
        });
    }
}
