using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace IdiotProof.Core.Catalysts;

/// <summary>One press release from a wire's RSS or Atom feed.</summary>
public sealed record WireFeedItem(string Id, string Title, string? Link, DateTime? PublishedUtc, string? Summary, IReadOnlyList<string> Categories);

/// <summary>
/// Parses the press-release wires' RSS 2.0 / Atom feeds (IP-T602) and pulls US tickers out of them: exchange:ticker
/// patterns ("(NASDAQ: ABCD)", "NYSE American: XYZ", "OTCQB: ABCDF") in the title, summary and categories. Non-US
/// listings (TSX, TSXV, CSE, ASX, LSE...) are not tickers here: a Canadian release's OTC symbol is the one kept.
/// </summary>
public static partial class WireFeed
{
    public static IReadOnlyList<WireFeedItem> Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException)
        {
            return [];
        }
        var root = doc.Root;
        if (root is null) return [];
        var items = root.Descendants().Where(e => e.Name.LocalName is "item" or "entry").ToList();
        var result = new List<WireFeedItem>(items.Count);
        foreach (var e in items)
        {
            var title = Clean(Child(e, "title"));
            if (string.IsNullOrWhiteSpace(title)) continue;
            var link = Child(e, "link") is { Length: > 0 } l ? l.Trim()
                : e.Elements().FirstOrDefault(x => x.Name.LocalName == "link" && (string?)x.Attribute("rel") is null or "alternate")?.Attribute("href")?.Value;
            var id = Child(e, "guid") ?? Child(e, "id") ?? link ?? title;
            var published = Time(Child(e, "pubDate")) ?? Time(Child(e, "published")) ?? Time(Child(e, "updated")) ?? Time(Child(e, "date"));
            var summary = Clean(Child(e, "description") ?? Child(e, "summary") ?? Child(e, "encoded") ?? Child(e, "content"));
            var categories = e.Elements()
                .Where(x => x.Name.LocalName is "category" or "subject" or "keywords")
                .Select(x => (x.Value is { Length: > 0 } v ? v : (string?)x.Attribute("term") ?? "").Trim())
                .Where(x => x.Length > 0).Distinct().ToList();
            result.Add(new WireFeedItem(id.Trim(), title!, link, published, summary, categories));
        }
        return result;
    }

    /// <summary>US tickers named with an exchange prefix, in order of first mention.</summary>
    public static IReadOnlyList<string> Tickers(WireFeedItem item) =>
        Tickers(new[] { item.Title }.Concat(item.Categories).Append(item.Summary ?? ""));

    public static IReadOnlyList<string> Tickers(IEnumerable<string> texts)
    {
        var found = new List<string>();
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match m in ExchangeTicker().Matches(text))
            {
                foreach (var raw in TickerSeparator().Split(m.Groups["tickers"].Value))
                {
                    var t = raw.Trim().TrimStart('$');
                    if (TickerShape().IsMatch(t) && !found.Contains(t)) found.Add(t);
                }
            }
        }
        return found;
    }

    /// <summary>Lowercase alphanumeric tokens of a headline without the wire's boilerplate, for cross-source matching.</summary>
    public static HashSet<string> Tokens(string headline)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Word().Matches(headline.ToLowerInvariant()))
            if (m.Value.Length > 1 && !StopWords.Contains(m.Value)) set.Add(m.Value);
        return set;
    }

    /// <summary>Jaccard similarity of two headlines' tokens (0..1); a headline that contains the other scores at least 0.9.</summary>
    public static double Similarity(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return 0;
        if (na == nb) return 1;
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var inter = ta.Count(tb.Contains);
        var jaccard = (double)inter / (ta.Count + tb.Count - inter);
        var shorter = na.Length <= nb.Length ? na : nb;
        var longer = ReferenceEquals(shorter, na) ? nb : na;
        if (shorter.Length >= 30 && longer.Contains(shorter, StringComparison.Ordinal)) return Math.Max(jaccard, 0.9);
        return jaccard;
    }

    private static string Normalize(string s) => string.Join(' ', Word().Matches(s.ToLowerInvariant()).Select(m => m.Value));

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "of", "to", "in", "on", "with", "its", "inc", "corp", "corporation", "ltd", "llc", "plc", "co",
        "announces", "announce", "announced", "reports", "report", "nasdaq", "nyse", "otcqb", "otcqx", "otc", "amex", "american",
        "press", "release", "newswire", "globe", "prnewswire", "business", "wire", "newsfile", "accesswire", "an", "at", "by", "as", "is",
    };

    private static string? Child(XElement e, string localName) =>
        e.Elements().FirstOrDefault(x => x.Name.LocalName == localName) is { } c && !c.IsEmpty ? c.Value : null;

    private static string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = WebUtility.HtmlDecode(Tag().Replace(html, " "));
        text = Space().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static DateTime? Time(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        // Business Wire stamps "... 01:44:00 UT"; .NET reads "GMT" but not "UT".
        if (t.EndsWith(" UT", StringComparison.Ordinal)) t = t[..^3] + " GMT";
        if (DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;
        // RFC 822 with a zone name .NET does not know ("EST", "EDT"): drop the name and apply the offset.
        var zone = ZoneName().Match(t);
        if (zone.Success && Zones.TryGetValue(zone.Groups[1].Value, out var offset)
            && DateTime.TryParse(t[..zone.Index], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        return null;
    }

    private static readonly Dictionary<string, TimeSpan> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EST"] = TimeSpan.FromHours(-5), ["EDT"] = TimeSpan.FromHours(-4), ["CST"] = TimeSpan.FromHours(-6), ["CDT"] = TimeSpan.FromHours(-5),
        ["MST"] = TimeSpan.FromHours(-7), ["MDT"] = TimeSpan.FromHours(-6), ["PST"] = TimeSpan.FromHours(-8), ["PDT"] = TimeSpan.FromHours(-7),
        ["ET"] = TimeSpan.FromHours(-5), ["UTC"] = TimeSpan.Zero, ["Z"] = TimeSpan.Zero,
    };

    // The exchange is matched case-insensitively; the tickers after it must be capitals.
    [GeneratedRegex(@"(?<![A-Za-z])(?i:nasdaq(?:\s?(?:gs|gm|cm|capital\s+market|global\s+(?:select\s+)?market))?|nyse(?:\s?(?:american|mkt|arca))?|amex|otc(?:qb|qx|\s?pink|\s?markets|\s?bb)?|cboe(?:\s?bzx)?)\s*[:：]\s*(?<tickers>\$?[A-Z]{1,5}(?:[.\-][A-Z]{1,2})?(?:\s*(?:,|/|&|\band\b)\s*\$?[A-Z]{1,5}(?:[.\-][A-Z]{1,2})?)*)(?![A-Za-z])")]
    private static partial Regex ExchangeTicker();

    [GeneratedRegex(@"\s*(?:,|/|&|\band\b)\s*")]
    private static partial Regex TickerSeparator();

    [GeneratedRegex(@"^[A-Z]{1,5}(?:[.\-][A-Z]{1,2})?$")]
    private static partial Regex TickerShape();

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"\s([A-Za-z]{1,4})$")]
    private static partial Regex ZoneName();
}
