using System.Text.RegularExpressions;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>
/// The study's Form 4 source: the complete submission text file of a filing
/// (<c>{accession}.txt</c>), which embeds the ownership XML the parser reads, so one fetch per
/// filing needs no directory listing. Claims written from it carry <see cref="SourceName"/> so the
/// snapshot stage can find them beside the Research tab's own scan rows.
/// </summary>
public static partial class Form4Submission
{
    public const string SourceName = "SEC EDGAR Form 4 (gapper study)";
    public const string ClaimType = "Insider";
    public const string FormType = "4";

    public static string Url(string cik, string accession) =>
        $"https://www.sec.gov/Archives/edgar/data/{Catalysts.EdgarTickerMap.Normalize(cik).TrimStart('0')}/{accession.Replace("-", "")}/{accession}.txt";

    /// <summary>The <c>&lt;ownershipDocument&gt;</c> element of a submission, or null when the text carries none.</summary>
    public static string? ExtractOwnershipXml(string submissionText)
    {
        if (string.IsNullOrEmpty(submissionText)) return null;
        var m = OwnershipPattern().Match(submissionText);
        if (!m.Success) return null;
        var xml = m.Value;
        if (xml.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return xml;
        var declaration = DeclarationPattern().Match(submissionText[..m.Index]);
        return declaration.Success ? declaration.Value + xml : xml;
    }

    /// <summary>"0001234567-24-000012" inside a URL or text, or null.</summary>
    public static string? AccessionOf(string? text) =>
        text is null ? null : AccessionPattern().Match(text) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"<ownershipDocument\b[\s\S]*?</ownershipDocument>", RegexOptions.IgnoreCase)]
    private static partial Regex OwnershipPattern();

    [GeneratedRegex(@"<\?xml[^>]*\?>", RegexOptions.IgnoreCase | RegexOptions.RightToLeft)]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex(@"\d{10}-\d{2}-\d{6}")]
    private static partial Regex AccessionPattern();
}
