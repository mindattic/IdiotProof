using IdiotProof.Core.Evidence;
using IdiotProof.Models;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442: the packed minute-bar file store round-trips, ignores duplicate days, marks empty days, and survives a truncated tail.</summary>
[TestFixture]
public class FileBackedBarStoreTests
{
    private string root = null!;

    [SetUp]
    public void SetUp() => root = Path.Combine(Path.GetTempPath(), "gapper-store-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Test]
    public void RoundTrip_KeepsEveryField_IncludingTradeCountVwapAndProvenance()
    {
        var store = new FileBackedBarStore(root);
        var bars = new List<Candle> { Bar(Day, 4, 0, 1.2345, 1500.5, 1.25, 1.2, 1.21), Bar(Day, 9, 30, 0.000123, 10, note: "Alpaca:sip") };
        Assert.That(store.AppendDay("gogo", Day, bars), Is.True);

        var read = store.ReadDay("GOGO", Day).Cast<DetailedCandle>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(read, Has.Count.EqualTo(2));
            Assert.That(read[0].StartUtc, Is.EqualTo(bars[0].StartUtc));
            Assert.That(read[0].StartUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(read[0].Open, Is.EqualTo(1.21m));
            Assert.That(read[0].High, Is.EqualTo(1.25m));
            Assert.That(read[0].Low, Is.EqualTo(1.2m));
            Assert.That(read[0].Close, Is.EqualTo(1.2345m));
            Assert.That(read[0].Volume, Is.EqualTo(1500.5m));
            Assert.That(read[0].TradeCount, Is.EqualTo(3));
            Assert.That(read[0].Vwap, Is.EqualTo(1.2345m));
            Assert.That(read[0].Note, Is.EqualTo("Alpaca:sip"));
            Assert.That(read[1].Close, Is.EqualTo(0.000123m), "six decimals survive");
            Assert.That(File.Exists(Path.Combine(root, "GOGO", "2026-07.bin")), Is.True);
        });
    }

    [Test]
    public void ReAppend_IsIdempotent_AndEmptyDaysAreCoverageMarks()
    {
        var store = new FileBackedBarStore(root);
        var next = Day.AddDays(3);
        store.AppendDay("T", Day, [Bar(Day, 4, 0, 2, 100)]);
        Assert.That(store.AppendDay("T", Day, [Bar(Day, 4, 1, 9, 9)]), Is.False, "a day already present is never rewritten");
        store.AppendDay("T", next, []);

        Assert.Multiple(() =>
        {
            Assert.That(store.ReadDay("T", Day).Single().Close, Is.EqualTo(2m));
            Assert.That(store.HasDay("T", next), Is.True, "an empty day still counts as fetched");
            Assert.That(store.CoveredDays("T", Day, next.AddDays(1)), Is.EquivalentTo(new[] { Day, next }));
            Assert.That(store.ReadDays("T", Day, next), Has.Count.EqualTo(1));
            var cov = store.Coverage("T");
            Assert.That((cov.Days, cov.EmptyDays, cov.Bars, cov.SipBars), Is.EqualTo((2, 1, 1L, 1L)));
            Assert.That(cov.SipShare, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void TruncatedTrailingBlock_IsDropped_AndTheDayCanBeReappended()
    {
        var store = new FileBackedBarStore(root);
        store.AppendDay("T", Day, [Bar(Day, 4, 0, 2, 100)]);
        var next = Day.AddDays(1);
        store.AppendDay("T", next, [Bar(next, 4, 0, 3, 100), Bar(next, 4, 1, 3.1, 100)]);
        var path = Path.Combine(root, "T", "2026-07.bin");
        using (var f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)) f.SetLength(f.Length - 20);

        Assert.That(store.HasDay("T", next), Is.False, "the cut block is not coverage");
        Assert.That(store.ReadDay("T", Day).Single().Close, Is.EqualTo(2m));
        Assert.That(store.AppendDay("T", next, [Bar(next, 4, 0, 3, 100)]), Is.True);
        Assert.That(store.ReadDay("T", next).Single().Close, Is.EqualTo(3m));
    }

    [Test]
    public void MonthsAreSeparateFiles_AndReadsSpanThem()
    {
        var store = new FileBackedBarStore(root);
        var july = new DateOnly(2026, 7, 31);
        var august = new DateOnly(2026, 8, 3);
        store.AppendDay("T", july, [Bar(july, 4, 0, 1, 1)]);
        store.AppendDay("T", august, [Bar(august, 4, 0, 2, 1)]);
        Assert.That(store.ReadDays("T", july, august).Select(b => b.Close), Is.EqualTo(new[] { 1m, 2m }));
        Assert.That(Directory.GetFiles(Path.Combine(root, "T")).Select(Path.GetFileName), Is.EquivalentTo(new[] { "2026-07.bin", "2026-08.bin" }));
    }
}
