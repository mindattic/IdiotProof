using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Evidence.Tests;

/// <summary>IP-T458: ATM / baby-shelf detection over synthetic 424B5 text, and point-in-time dilution features.</summary>
public class DilutionFilingsTests
{
    private const string AtmBabyShelf424B5 = """
        <SEC-DOCUMENT>0001234567-24-000050.txt
        <TYPE>424B5
        <html><body>
        <p>Filed Pursuant to Rule 424(b)(5)<br/>Registration No. 333-270000</p>
        <p><b>PROSPECTUS SUPPLEMENT</b> (To Prospectus dated May&nbsp;1, 2023)</p>
        <p>Up to $5,250,000</p><p>Common Stock</p>
        <p>We have entered into an At The Market Offering Agreement, or the sales agreement, with H.C. Wainwright &amp; Co., LLC, or the sales agent,
        relating to shares of our common stock offered by this prospectus supplement. In accordance with the terms of the sales agreement,
        we may offer and sell shares of our common stock having an aggregate offering price of up to $5,250,000 from time to time through the sales agent.</p>
        <p>Sales of our common stock, if any, under this prospectus supplement will be made in sales deemed to be an &#8220;at the market offering&#8221;
        as defined in Rule 415(a)(4) promulgated under the Securities Act of 1933.</p>
        <p>As of March 1, 2024, the aggregate market value of our outstanding common stock held by non-affiliates, or public float, was approximately
        $18.9 million, based on 6,300,000 shares of outstanding common stock held by non-affiliates and a price of $3.00 per share.
        We have sold securities with an aggregate market value of approximately $1.05 million pursuant to General Instruction I.B.6 of Form S-3
        during the prior 12 calendar month period that ends on and includes the date of this prospectus supplement.
        Pursuant to General Instruction I.B.6 of Form S-3, in no event will we sell securities in a public primary offering with a value exceeding
        more than one-third of our public float in any 12-month period so long as our public float remains below $75.0 million.</p>
        </body></html>
        """;

    private const string RegisteredDirect424B5 = """
        <TYPE>424B5
        PROSPECTUS SUPPLEMENT
        2,000,000 Shares of Common Stock
        We are offering 2,000,000 shares of our common stock directly to certain institutional investors in a registered direct offering
        pursuant to this prospectus supplement and the accompanying prospectus and a securities purchase agreement.
        We have engaged Maxim Group LLC to act as our exclusive placement agent. The offering price is $1.25 per share.
        """;

    [Test]
    public void Detect_FlagsAnAtm_WithSizeFloatAndBabyShelfHeadroom()
    {
        var a = AtmShelfDetector.Detect(AtmBabyShelf424B5);
        Assert.Multiple(() =>
        {
            Assert.That(a.IsAtm, Is.True);
            Assert.That(a.AtmOnCover, Is.True);
            Assert.That(a.Phrases, Does.Contain("at the market offering"));
            Assert.That(a.Phrases, Does.Contain("sales agreement"));
            Assert.That(a.OfferingUsd, Is.EqualTo(5_250_000).Within(1e-6));
            Assert.That(a.BabyShelf, Is.True);
            Assert.That(a.PublicFloatUsd, Is.EqualTo(18_900_000).Within(1e-3));
            Assert.That(a.SoldPrior12mUsd, Is.EqualTo(1_050_000).Within(1e-3));
            Assert.That(a.CapacityUsd, Is.EqualTo(6_300_000).Within(1e-3), "one third of $18.9M");
            Assert.That(a.HeadroomUsd, Is.EqualTo(5_250_000).Within(1e-3));
        });
    }

    [Test]
    public void Detect_LeavesARegisteredDirectAlone()
    {
        var a = AtmShelfDetector.Detect(RegisteredDirect424B5);
        Assert.Multiple(() =>
        {
            Assert.That(a.IsAtm, Is.False);
            Assert.That(a.BabyShelf, Is.False);
            Assert.That(a.HeadroomUsd, Is.Null);
        });
    }

    [Test]
    public void Detect_ReadsMillionsAndTheNothingSoldStatement()
    {
        var a = AtmShelfDetector.Detect("""
            We have entered into an Equity Distribution Agreement with Canaccord Genuity LLC, as sales agent, under which we may sell
            shares having an aggregate offering price of up to $12.5 million. The aggregate market value of our outstanding common stock held by
            non-affiliates is $30,000,000. We have not offered or sold any securities pursuant to General Instruction I.B.6 of Form S-3 during the
            12 calendar months prior to and including the date of this prospectus supplement.
            """);
        Assert.Multiple(() =>
        {
            Assert.That(a.IsAtm, Is.True);
            Assert.That(a.OfferingUsd, Is.EqualTo(12_500_000).Within(1e-3));
            Assert.That(a.PublicFloatUsd, Is.EqualTo(30_000_000).Within(1e-3));
            Assert.That(a.SoldPrior12mUsd, Is.EqualTo(0));
            Assert.That(a.HeadroomUsd, Is.EqualTo(10_000_000).Within(1e-3));
        });
        Assert.That(AtmShelfDetector.Detect(null), Is.SameAs(AtmShelfFacts.None));
    }

    [Test]
    public void ClassifiedLine_RoundTrips()
    {
        var a = AtmShelfDetector.Detect(AtmBabyShelf424B5);
        var back = DilutionHistory.ParseClassified(DilutionHistory.ClassifiedLine("0001234567-24-000050", a))!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(back.Accession, Is.EqualTo("0001234567-24-000050"));
            Assert.That(back.Facts.IsAtm, Is.True);
            Assert.That(back.Facts.OfferingUsd, Is.EqualTo(5_250_000));
            Assert.That(back.Facts.HeadroomUsd, Is.EqualTo(5_250_000).Within(1));
            Assert.That(back.Facts.Phrases, Is.EquivalentTo(a.Phrases));
        });
    }

    [Test]
    public void Features_UseOnlyFilingsKnownBeforeTheDecision()
    {
        const int cik = 1234567;
        var day = new DateOnly(2024, 3, 12); // Tuesday
        var cutoff = day.ToDateTime(new TimeOnly(8, 0));
        var filings = new List<DilutionFiling>
        {
            new(cik, "424B5", "atm", new DateOnly(2024, 3, 1), new DateTime(2024, 3, 1, 17, 5, 0)),
            new(cik, "6-K", "6k-old", new DateOnly(2024, 2, 20), null),
            new(cik, "6-K", "6k-after-close", new DateOnly(2024, 3, 11), new DateTime(2024, 3, 11, 18, 30, 0)),
            new(cik, "6-K", "6k-premarket-before-t", new DateOnly(2024, 3, 12), new DateTime(2024, 3, 12, 7, 15, 0)),
            new(cik, "6-K", "6k-after-t", new DateOnly(2024, 3, 12), new DateTime(2024, 3, 12, 8, 30, 0)),
            new(cik, "6-K", "6k-same-day-no-header", new DateOnly(2024, 3, 12), null),
            new(cik, "EFFECT", "effect", new DateOnly(2024, 2, 1), null),
            new(cik, "SC 13G", "13g", new DateOnly(2024, 3, 8), null),
            new(cik, "F-1/A", "f1a", new DateOnly(2024, 1, 2), null),
            new(cik, "424B7", "b7-today", new DateOnly(2024, 3, 12), null),
        };
        var classified = new Dictionary<string, AtmShelfFacts> { ["atm"] = AtmShelfDetector.Detect(AtmBabyShelf424B5) };
        var h = new DilutionHistory(filings, classified);
        var v = h.Features(cik, day, cutoff);
        double F(string name) => v[Array.IndexOf(DilutionFeatures.All, name)];
        Assert.Multiple(() =>
        {
            Assert.That(F(DilutionFeatures.AtmActive), Is.EqualTo(1));
            Assert.That(F(DilutionFeatures.DaysSinceAtm), Is.EqualTo(11));
            Assert.That(F(DilutionFeatures.AtmLogUsd), Is.EqualTo(Math.Log(5_250_000)).Within(1e-9));
            Assert.That(F(DilutionFeatures.BabyShelf), Is.EqualTo(1));
            Assert.That(F(DilutionFeatures.HeadroomFrac), Is.EqualTo(5.25 / 6.3).Within(1e-6));
            Assert.That(F(DilutionFeatures.SixKCount30), Is.EqualTo(3), "old, after-close and premarket-before-t; not the one after t nor the same-day one without a header");
            Assert.That(F(DilutionFeatures.SixKSinceClose), Is.EqualTo(1));
            Assert.That(F(DilutionFeatures.DaysSinceEffect), Is.EqualTo(40));
            Assert.That(F(DilutionFeatures.DaysSinceS1A), Is.EqualTo(70));
            Assert.That(F(DilutionFeatures.DaysSinceForeignReg), Is.EqualTo(70));
            Assert.That(F(DilutionFeatures.DaysSinceOther424B), Is.NaN, "filed on D with no acceptance time: not known");
            Assert.That(F(DilutionFeatures.Sc13gCount5), Is.EqualTo(1));
        });

        var before = h.Features(cik, new DateOnly(2024, 3, 1), new DateTime(2024, 3, 1, 17, 0, 0));
        Assert.That(before[Array.IndexOf(DilutionFeatures.All, DilutionFeatures.AtmActive)], Is.EqualTo(0), "the ATM prospectus was accepted at 17:05, after this cutoff");
        Assert.That(h.Features(999, day, cutoff).All(double.IsNaN), Is.True, "no filings on file for the CIK");
    }

    [Test]
    public void AtmActive_IsUnknown_WhileA424B5InTheWindowIsUnclassified()
    {
        var h = new DilutionHistory([new DilutionFiling(1, "424B5", "x", new DateOnly(2024, 1, 5), null)], new Dictionary<string, AtmShelfFacts>());
        var v = h.Features(1, new DateOnly(2024, 2, 1), new DateTime(2024, 2, 1, 8, 0, 0));
        Assert.That(v[Array.IndexOf(DilutionFeatures.All, DilutionFeatures.AtmActive)], Is.NaN);
    }

    [Test]
    public void Load_JoinsHeadersAndClassifications_FromTheFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dilution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, DilutionHistory.FilingsFile),
                [DilutionHistory.FilingsHeader, DilutionHistory.FilingLine(new DilutionFiling(42, "6-K", "a1", new DateOnly(2024, 3, 12), null)), "garbage"]);
            File.WriteAllLines(Path.Combine(dir, DilutionHistory.HeadersFile),
                [DilutionHistory.HeadersHeader, DilutionHistory.HeaderLine("a1", new DateTime(2024, 3, 12, 6, 45, 10))]);
            var h = DilutionHistory.Load(dir)!;
            var v = h.Features(42, new DateOnly(2024, 3, 12), new DateTime(2024, 3, 12, 7, 0, 0));
            Assert.Multiple(() =>
            {
                Assert.That(h.Count, Is.EqualTo(1));
                Assert.That(v[Array.IndexOf(DilutionFeatures.All, DilutionFeatures.SixKSinceClose)], Is.EqualTo(1), "06:45:10 ET header makes the same-day 6-K known at 07:00");
            });
            Assert.That(DilutionHistory.Load(Path.Combine(dir, "missing")), Is.Null);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void WidenedForms_AreNewToTheFrozenList_AndPulledAddsTheTextForms()
    {
        string[] frozen = ["8-K", "8-K/A", "4", "3", "S-1", "S-3", "424B3", "424B4", "424B5", "SC 13D", "25-NSE", "15-12G"];
        Assert.Multiple(() =>
        {
            Assert.That(DilutionForms.Widened.Intersect(frozen), Is.Empty);
            Assert.That(DilutionForms.Widened, Has.Count.EqualTo(13));
            Assert.That(DilutionForms.Pulled, Does.Contain("424B5").And.Contain("SC 13G/A").And.Contain("EFFECT"));
        });
    }
}
