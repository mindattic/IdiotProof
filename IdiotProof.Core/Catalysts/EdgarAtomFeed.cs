using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IdiotProof.Core.Catalysts;

/// <summary>One entry of EDGAR's latest-filings Atom feed (<c>browse-edgar?action=getcurrent&amp;output=atom</c>).</summary>
public sealed record EdgarAtomEntry(
    string FormType,
    string CompanyName,
    string Cik,
    string Role,
    string AccessionNumber,
    string IndexUrl,
    DateTime? UpdatedUtc,
    string Summary);

/// <summary>Parses the latest-filings Atom feed. Pure; the poller fetches.</summary>
public static partial class EdgarAtomFeed
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public static List<EdgarAtomEntry> Parse(string atomXml)
    {
        var entries = new List<EdgarAtomEntry>();
        XDocument doc;
        try { doc = XDocument.Parse(atomXml); }
        catch (System.Xml.XmlException) { return entries; }

        foreach (var entry in doc.Descendants(Atom + "entry"))
        {
            var title = entry.Element(Atom + "title")?.Value?.Trim() ?? "";
            var link = entry.Element(Atom + "link")?.Attribute("href")?.Value?.Trim() ?? "";
            var id = entry.Element(Atom + "id")?.Value ?? "";
            var category = entry.Element(Atom + "category")?.Attribute("term")?.Value?.Trim();
            var summary = StripTags(entry.Element(Atom + "summary")?.Value ?? "");
            var updated = entry.Element(Atom + "updated")?.Value is { } u && DateTime.TryParse(u, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTime?)null;

            var m = TitlePattern().Match(title);
            var form = category ?? (m.Success ? m.Groups["form"].Value : "");
            var name = m.Success ? m.Groups["name"].Value.Trim() : title;
            var cik = m.Success ? m.Groups["cik"].Value : "";
            var role = m.Success ? m.Groups["role"].Value : "";

            var accession = AccessionPattern().Match(id) is { Success: true } a ? a.Groups[1].Value
                : AccessionPattern().Match(link) is { Success: true } b ? b.Groups[1].Value
                : AccessionPattern().Match(summary) is { Success: true } c ? c.Groups[1].Value : "";
            if (accession.Length == 0 || form.Length == 0) continue;
            entries.Add(new EdgarAtomEntry(form, name, cik, role, accession, link, updated, summary));
        }
        return entries;
    }

    /// <summary>
    /// Form 4s appear once per party (reporting owner and issuer); this keeps one entry per accession,
    /// preferring the issuer's so the CIK maps to the traded ticker.
    /// </summary>
    public static List<EdgarAtomEntry> OnePerAccession(IEnumerable<EdgarAtomEntry> entries) =>
        entries.GroupBy(e => e.AccessionNumber)
            .Select(g => g.OrderBy(e => RoleRank(e.Role)).First())
            .ToList();

    private static int RoleRank(string role) => role.ToLowerInvariant() switch
    {
        "issuer" => 0,
        "filer" => 1,
        "subject" => 2,
        _ => 3,
    };

    [GeneratedRegex(@"^(?<form>.+?)\s+-\s+(?<name>.+?)\s+\((?<cik>\d{10})\)\s+\((?<role>[^)]+)\)\s*$")]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"(\d{10}-\d{2}-\d{6})")]
    private static partial Regex AccessionPattern();

    private static string StripTags(string html) =>
        Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ").Trim();
}
