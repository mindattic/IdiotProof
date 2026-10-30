using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Tests;

/// <summary>The text render kit (IP-T56).</summary>
public class RenderKitTests
{
    [TestCase(1234.56, "$1,234.56")]
    [TestCase(-1234.5, "-$1,234.50")]
    [TestCase(0.004, "$0.00")]
    [TestCase(-0.004, "$0.00")]
    [TestCase(1000000, "$1,000,000.00")]
    public void Money(decimal value, string expected) => Assert.That(Fmt.Money(value), Is.EqualTo(expected));

    [Test]
    public void Money_IgnoresCurrentCulture()
    {
        var saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.That(Fmt.Money(1234.56m), Is.EqualTo("$1,234.56"));
            Assert.That(Fmt.Percent(0.5m), Is.EqualTo("50.00%"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = saved;
        }
    }

    [Test]
    public void SignedMoney_And_Percent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fmt.SignedMoney(12m), Is.EqualTo("+$12.00"));
            Assert.That(Fmt.SignedMoney(-12m), Is.EqualTo("-$12.00"));
            Assert.That(Fmt.Percent(0.0525m), Is.EqualTo("5.25%"));
            Assert.That(Fmt.Percent(0.0525m, 1, signed: true), Is.EqualTo("+5.3%"));
            Assert.That(Fmt.Percent(-0.1, 0), Is.EqualTo("-10%"));
            Assert.That(Fmt.PercentPoints(5.25m), Is.EqualTo("5.25%"));
            Assert.That(Fmt.Number(1250m), Is.EqualTo("1,250"));
            Assert.That(Fmt.Number(0.5m), Is.EqualTo("0.5"));
        });
    }

    [Test]
    public void Et_FormatsInEasternAcrossDst()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fmt.Et(new DateTime(2026, 10, 30, 13, 31, 0, DateTimeKind.Utc)), Is.EqualTo("2026-10-30 9:31 AM ET"));
            Assert.That(Fmt.Et(new DateTime(2026, 12, 1, 20, 5, 0, DateTimeKind.Utc)), Is.EqualTo("2026-12-01 3:05 PM ET"));
            Assert.That(Fmt.Et(new DateTimeOffset(2026, 10, 30, 13, 31, 0, TimeSpan.Zero)), Is.EqualTo("2026-10-30 9:31 AM ET"));
            Assert.That(Fmt.Et((DateTime?)null), Is.EqualTo("—"));
            Assert.That(Fmt.EtTime(new DateTime(2026, 10, 30, 19, 55, 0, DateTimeKind.Utc)), Is.EqualTo("3:55 PM ET"));
        });
    }

    [Test]
    public void Table_AutoWidthsAndAlignment()
    {
        var table = new TextTable()
            .Column("Ticker").Column("Qty", Align.Right).Column("P&L", Align.Right)
            .Row("AAPL", "1", "+$12.00")
            .Row("NVDA", "250", "-$3.10");
        var lines = table.Render().Split(Environment.NewLine);
        Assert.That(lines, Is.EqualTo(new[]
        {
            "Ticker  Qty      P&L",
            "------  ---  -------",
            "AAPL      1  +$12.00",
            "NVDA    250   -$3.10",
        }));
    }

    [Test]
    public void Table_TruncatesWidestColumnToFitWithEllipsis()
    {
        var table = new TextTable { MaxWidth = 20 }
            .Column("Name").Column("Note")
            .Row("Gap and go", "a very long free-text note that will not fit");
        var lines = table.Render().Split(Environment.NewLine);
        Assert.That(lines.Max(l => l.Length), Is.LessThanOrEqualTo(20));
        Assert.That(lines[2], Does.EndWith("…"));
    }

    [Test]
    public void Table_PerColumnCap_And_CleansNewlines()
    {
        var text = new TextTable().Column("N", maxWidth: 5).Row("abcdefgh\nij").Render();
        Assert.That(text.Split(Environment.NewLine)[2], Is.EqualTo("abcd…"));
    }

    [Test]
    public void Table_RowWidthMismatchThrows() =>
        Assert.Throws<ArgumentException>(() => new TextTable().Column("A").Row("1", "2"));

    [Test]
    public void Color_OnlyWhenEnabled()
    {
        var plain = new TextTable().Column("A").Row("x").Style(0, new Ansi(false).Red).Render();
        Assert.That(plain, Does.Not.Contain("\u001b["));

        var on = new Ansi(true);
        var colored = new TextTable { Ansi = on }.Column("A").Row("x").Style(0, on.Red).Render();
        Assert.That(colored, Does.Contain("\u001b[31mx\u001b[0m").And.Contain("\u001b[1mA\u001b[0m"));
        Assert.That(new Ansi(true).Signed(-1m, "-$1.00"), Does.StartWith("\u001b[31m"));
        Assert.That(new Ansi(false).Signed(-1m, "-$1.00"), Is.EqualTo("-$1.00"));
    }

    [Test]
    public void KeyValueBlock_AlignsAndSkipsNulls()
    {
        var text = new KeyValueBlock()
            .Add("Ticker", "AAPL")
            .Add("Mode", null)
            .Add("Limit price", null, "—")
            .Add("Notes", "line one\nline two")
            .Render();
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(new[]
        {
            "Ticker:       AAPL",
            "Limit price:  —",
            "Notes:        line one",
            "              line two",
        }));
    }
}
