using IdiotProof.Core.Research.Gappers;
using static IdiotProof.Core.Tests.GapperStudy.GapperStudyTestKit;

namespace IdiotProof.Core.Tests.GapperStudy;

/// <summary>IP-T442: the quarterly form.idx parses by the header's column edges and keeps only the frozen form list when asked.</summary>
[TestFixture]
public class EdgarFormIndexTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "edgar-form-idx-sample.txt");

    [Test]
    public void Parses_EveryRow_WithFormCompanyCikDateAndAccession()
    {
        using var reader = new StreamReader(FixturePath);
        var rows = EdgarFormIndex.Parse(reader).ToList();
        Assert.That(rows, Has.Count.EqualTo(9));
        var gogo8k = rows.Single(r => r.FormType == "8-K");
        Assert.Multiple(() =>
        {
            Assert.That(gogo8k.CompanyName, Is.EqualTo("GOGO INC"));
            Assert.That(gogo8k.Cik, Is.EqualTo("1537054"));
            Assert.That(gogo8k.FiledDate, Is.EqualTo(new DateOnly(2026, 11, 5)));
            Assert.That(gogo8k.AccessionNumber, Is.EqualTo("0001537054-26-000077"));
            Assert.That(rows.Single(r => r.FormType == "SC 13D").CompanyName, Is.EqualTo("Activist Fund, L.P."), "a form type with a space keeps its company name intact");
            Assert.That(rows.Single(r => r.FormType == "8-K/A").FiledDate, Is.EqualTo(new DateOnly(2026, 11, 6)));
            Assert.That(rows.Count(r => r.AccessionNumber == "0001209191-26-061234"), Is.EqualTo(2), "a Form 4 lists both the issuer and the reporting owner");
            Assert.That(rows.Single(r => r.AccessionNumber == "0001537054-26-000008").FiledDate, Is.EqualTo(new DateOnly(2026, 10, 1)), "the body pads the form type wider than the header does");
        });
    }

    [Test]
    public void FormFilter_KeepsOnlyTheFrozenList()
    {
        using var reader = new StreamReader(FixturePath);
        var forms = Params.DataTiers.EdgarForms.ToHashSet(StringComparer.Ordinal);
        var rows = EdgarFormIndex.Parse(reader, forms).ToList();
        Assert.That(rows.Select(r => r.FormType).Distinct(), Is.EquivalentTo(new[] { "4", "424B4", "8-K", "8-K/A", "SC 13D" }));
        Assert.That(rows.Any(r => r.FormType is "1-A" or "10-K"), Is.False);
    }

    [Test]
    public void Quarters_CoverTheRange_AndTheStudySpanIs22()
    {
        Assert.That(EdgarFormIndex.Quarters(new DateOnly(2024, 11, 15), new DateOnly(2025, 4, 1)), Is.EqualTo(new[] { (2024, 4), (2025, 1), (2025, 2) }));
        var span = Params.DataTiers.DailyBars;
        Assert.That(EdgarFormIndex.Quarters(span.From, span.To), Has.Count.EqualTo(Params.DataTiers.EdgarFullIndexQuarters));
        Assert.That(EdgarFormIndex.Url(2026, 4), Is.EqualTo("https://www.sec.gov/Archives/edgar/full-index/2026/QTR4/form.idx"));
    }
}
