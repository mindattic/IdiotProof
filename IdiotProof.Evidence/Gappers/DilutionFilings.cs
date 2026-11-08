using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// What a 424B5 (or S-3/F-3) document says about at-the-market selling and the baby-shelf cap
/// (avenues.md A2, IP-T458). Dollar values are null when the text does not state them.
/// </summary>
public sealed record AtmShelfFacts(
    bool IsAtm,
    bool AtmOnCover,
    double? OfferingUsd,
    bool BabyShelf,
    double? PublicFloatUsd,
    double? SoldPrior12mUsd,
    IReadOnlyList<string> Phrases)
{
    /// <summary>Instruction I.B.6 capacity: one third of the stated public float, when the text states the limit and the float.</summary>
    public double? CapacityUsd => BabyShelf && PublicFloatUsd is { } f ? f / 3 : null;

    /// <summary>Capacity left for the 12 months: one third of public float minus what the text says was sold under I.B.6 (null when either is missing).</summary>
    public double? HeadroomUsd => CapacityUsd is { } c && SoldPrior12mUsd is { } s ? c - s : null;

    public static readonly AtmShelfFacts None = new(false, false, null, false, null, null, []);
}

/// <summary>
/// Pure ATM / shelf detector over prospectus text (IP-T458). An at-the-market program shows up as a
/// 424B5 whose cover names an "at the market offering" (Rule 415(a)(4)) sold through a sales agent
/// under a "sales agreement", "equity distribution agreement", "open market sale agreement" or
/// "controlled equity offering" agreement. The offering size is the first "aggregate offering price of
/// up to $X" (or "up to $X") on the cover. A baby shelf is a prospectus that cites Form S-3/F-3 General
/// Instruction I.B.6 (issuers with public float under $75M may sell at most one third of it per 12
/// months); such a supplement states the public float and what was sold under I.B.6 in the prior 12
/// calendar months. Regex only: a hand-labelled check set comes before any confirmatory use.
/// </summary>
public static partial class AtmShelfDetector
{
    /// <summary>Characters of normalized text treated as the cover page.</summary>
    public const int CoverChars = 20_000;

    public static AtmShelfFacts Detect(string? documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText)) return AtmShelfFacts.None;
        var text = Normalize(documentText);
        var phrases = new List<string>();
        int firstAtm = int.MaxValue;

        foreach (var (name, pattern) in AtmPatterns)
        {
            var m = pattern.Match(text);
            if (!m.Success) continue;
            phrases.Add(name);
            firstAtm = Math.Min(firstAtm, m.Index);
        }
        // "sales agreement" alone appears in non-ATM deals; it counts with a sales agent or an ATM acronym.
        bool strong = phrases.Contains("at the market offering") || phrases.Contains("rule 415(a)(4)") || phrases.Contains("controlled equity offering")
            || phrases.Contains("open market sale agreement") || phrases.Contains("at market issuance sales agreement")
            || (phrases.Any(p => p is "sales agreement" or "equity distribution agreement" or "ATM") && SalesAgentPattern().IsMatch(text));
        bool isAtm = strong;

        double? offering = null;
        var cover = text.Length > CoverChars ? text[..CoverChars] : text;
        if (AggregateOfferingPattern().Match(cover) is { Success: true } ao) offering = Money(ao.Groups["n"].Value, ao.Groups["u"].Value);
        else if (UpToPattern().Match(cover) is { Success: true } up) offering = Money(up.Groups["n"].Value, up.Groups["u"].Value);

        bool babyShelf = InstructionIb6Pattern().IsMatch(text);
        double? publicFloat = PublicFloatPattern().Match(text) is { Success: true } pf ? Money(pf.Groups["n"].Value, pf.Groups["u"].Value) : null;
        double? sold = null;
        if (babyShelf)
        {
            if (NoneSoldPattern().IsMatch(text)) sold = 0;
            else if (SoldPattern().Match(text) is { Success: true } sm) sold = Money(sm.Groups["n"].Value, sm.Groups["u"].Value);
        }
        return new AtmShelfFacts(isAtm, isAtm && firstAtm < CoverChars, offering, babyShelf, publicFloat, sold, phrases);
    }

    /// <summary>Strips SGML/HTML tags, decodes entities, folds typographic quotes, dashes and non-breaking spaces, and collapses whitespace.</summary>
    public static string Normalize(string raw)
    {
        var text = TagPattern().Replace(raw, " ");
        text = WebUtility.HtmlDecode(text);
        text = text.Replace(' ', ' ').Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
            .Replace('‐', '-').Replace('‑', '-').Replace('‒', '-').Replace('–', '-').Replace('—', '-');
        return SpacePattern().Replace(text, " ").Trim();
    }

    /// <summary>"$50,000,000" or "$25.0 million" → dollars; null when unparseable.</summary>
    public static double? Money(string number, string? unit)
    {
        if (!double.TryParse(number.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        return (unit ?? "").ToLowerInvariant() switch
        {
            "million" => v * 1e6,
            "billion" => v * 1e9,
            _ => v,
        };
    }

    private static readonly (string Name, Regex Pattern)[] AtmPatterns =
    [
        ("at the market offering", AtTheMarketPattern()),
        ("rule 415(a)(4)", Rule415a4Pattern()),
        ("sales agreement", SalesAgreementPattern()),
        ("equity distribution agreement", EquityDistributionPattern()),
        ("open market sale agreement", OpenMarketSalePattern()),
        ("controlled equity offering", ControlledEquityPattern()),
        ("at market issuance sales agreement", AtMarketIssuancePattern()),
        ("ATM", AtmAcronymPattern()),
    ];

    private const string Amount = @"\$\s?(?<n>\d[\d,]*(?:\.\d+)?)(?:\s*(?<u>million|billion))?";

    [GeneratedRegex(@"<[^>]{0,2000}>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();

    [GeneratedRegex(@"\bat[\s-]+the[\s-]+market\s+(?:offering|offerings|program|issuance|sales?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AtTheMarketPattern();

    [GeneratedRegex(@"Rule\s+415\s*\(a\)\s*\(4\)", RegexOptions.IgnoreCase)]
    private static partial Regex Rule415a4Pattern();

    [GeneratedRegex(@"\bsales\s+agreement\b", RegexOptions.IgnoreCase)]
    private static partial Regex SalesAgreementPattern();

    [GeneratedRegex(@"\bequity\s+distribution\s+agreement\b", RegexOptions.IgnoreCase)]
    private static partial Regex EquityDistributionPattern();

    [GeneratedRegex(@"\bopen\s+market\s+sale\s+agreement\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpenMarketSalePattern();

    [GeneratedRegex(@"\bcontrolled\s+equity\s+offering\b", RegexOptions.IgnoreCase)]
    private static partial Regex ControlledEquityPattern();

    [GeneratedRegex(@"\bat\s+market\s+issuance\s+sales\s+agreement\b", RegexOptions.IgnoreCase)]
    private static partial Regex AtMarketIssuancePattern();

    /// <summary>Upper-case ATM as its own word ("ATM Agreement", "ATM program"), case-sensitive.</summary>
    [GeneratedRegex(@"\bATM\b")]
    private static partial Regex AtmAcronymPattern();

    [GeneratedRegex(@"\b(?:sales\s+agent|placement\s+agent|distribution\s+agent|sales\s+manager|agent)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SalesAgentPattern();

    [GeneratedRegex(@"aggregate\s+(?:offering|gross\s+sales?|sales)\s+price\s+of\s+(?:up\s+to\s+)?" + Amount, RegexOptions.IgnoreCase)]
    private static partial Regex AggregateOfferingPattern();

    [GeneratedRegex(@"\bup\s+to\s+" + Amount, RegexOptions.IgnoreCase)]
    private static partial Regex UpToPattern();

    [GeneratedRegex(@"General\s+Instruction\s+I\.B\.6\b|\bInstruction\s+I\.B\.6\b|\bI\.B\.6\.?\s+of\s+Form\s+[SF]-3", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionIb6Pattern();

    [GeneratedRegex(@"aggregate\s+market\s+value\s+of\s+(?:our\s+|the\s+registrant's\s+)?(?:outstanding\s+)?(?:voting\s+and\s+non-voting\s+)?(?:common\s+(?:stock|equity|shares)|ordinary\s+shares)[^$]{0,250}?held\s+by\s+non-affiliates[^$]{0,200}?" + Amount, RegexOptions.IgnoreCase)]
    private static partial Regex PublicFloatPattern();

    [GeneratedRegex(@"(?:have|has)\s+not\s+(?:offered\s+(?:or|and)\s+)?sold\s+any\s+(?:securities|shares)[^.]{0,200}?I\.B\.6", RegexOptions.IgnoreCase)]
    private static partial Regex NoneSoldPattern();

    [GeneratedRegex(@"(?:sold|offered\s+and\s+sold|issued)\s+(?:securities|shares\s+of\s+(?:our\s+)?common\s+stock)?[^$.]{0,120}?" + Amount + @"[^.]{0,300}?I\.B\.6", RegexOptions.IgnoreCase)]
    private static partial Regex SoldPattern();
}

/// <summary>One filing of the widened EDGAR set (IP-T458): form, filing date and, when the header was fetched, the acceptance time in ET.</summary>
public sealed record DilutionFiling(int Cik, string FormType, string AccessionNumber, DateOnly Filed, DateTime? AcceptedEt)
{
    /// <summary>Known before the decision: accepted strictly before it when the acceptance time is on file, else filed strictly before D.</summary>
    public bool KnownBefore(DateOnly day, DateTime cutoffEt) => AcceptedEt is { } a ? a < cutoffEt : Filed < day;

    /// <summary>The day the filing became public: the acceptance date when on file, else the filing date.</summary>
    public DateOnly PublicDay => AcceptedEt is { } a ? DateOnly.FromDateTime(a) : Filed;
}

/// <summary>The forms of the widened pull and their groups (IP-T458). Kept apart from params.json's frozen <c>edgarForms</c>.</summary>
public static class DilutionForms
{
    /// <summary>The widened set (avenues.md A2): forms the frozen pull does not read.</summary>
    public static readonly IReadOnlyList<string> Widened =
        ["6-K", "6-K/A", "F-1", "F-1/A", "F-3", "F-3/A", "EFFECT", "424B1", "424B2", "424B7", "S-1/A", "SC 13G", "SC 13G/A"];

    /// <summary>Forms whose document text the ATM / shelf detector reads.</summary>
    public static readonly IReadOnlyList<string> TextForms = ["424B5", "S-3", "S-3/A", "F-3", "F-3/A"];

    /// <summary>Everything the dilution pull keeps from the quarterly index: the widened set plus the text forms, so its files stand alone.</summary>
    public static IReadOnlyList<string> Pulled => [.. Widened.Concat(TextForms).Distinct(StringComparer.Ordinal)];

    public static bool Is6K(string f) => f is "6-K" or "6-K/A";
    public static bool IsForeignRegistration(string f) => f is "F-1" or "F-1/A" or "F-3" or "F-3/A";
    public static bool IsS1Amendment(string f) => f is "S-1/A" or "F-1/A";
    public static bool IsOther424B(string f) => f is "424B1" or "424B2" or "424B7";
    public static bool IsSc13G(string f) => f is "SC 13G" or "SC 13G/A";
    public static bool IsShelf(string f) => f is "S-3" or "S-3/A" or "F-3" or "F-3/A";
}

public static class DilutionFeatures
{
    public const string AtmActive = "x.dilAtmActive";
    public const string DaysSinceAtm = "x.dilDaysSinceAtm";
    public const string AtmLogUsd = "x.dilAtmLogUsd";
    public const string BabyShelf = "x.dilBabyShelf";
    public const string HeadroomFrac = "x.dilShelfHeadroomFrac";
    public const string SixKCount30 = "x.dil6kCount30";
    public const string SixKSinceClose = "x.dil6kSinceClose";
    public const string DaysSinceEffect = "x.dilDaysSinceEffect";
    public const string DaysSinceS1A = "x.dilDaysSinceS1A";
    public const string DaysSinceForeignReg = "x.dilDaysSinceF1F3";
    public const string DaysSinceOther424B = "x.dilDaysSince424BOther";
    public const string Sc13gCount5 = "x.dilSc13gCount5";

    public static readonly string[] All =
        [AtmActive, DaysSinceAtm, AtmLogUsd, BabyShelf, HeadroomFrac, SixKCount30, SixKSinceClose, DaysSinceEffect, DaysSinceS1A, DaysSinceForeignReg, DaysSinceOther424B, Sc13gCount5];

    /// <summary>An ATM program counts as active for this many days after its last ATM prospectus (no termination detection yet).</summary>
    public const int AtmActiveDays = 365;
}

/// <summary>
/// Point-in-time dilution features over the widened filings and the detector's classifications
/// (IP-T458). Every feature at (D, t) reads only filings known before t: accepted strictly before t
/// when the acceptance time is on file, else filed strictly before D. NaN means unknown: the CIK has no
/// filings on file, or a 424B5 inside the ATM window has not been classified yet.
/// </summary>
public sealed class DilutionHistory
{
    private readonly Dictionary<int, List<DilutionFiling>> byCik;
    private readonly IReadOnlyDictionary<string, AtmShelfFacts> facts;

    public DilutionHistory(IEnumerable<DilutionFiling> filings, IReadOnlyDictionary<string, AtmShelfFacts> classified)
    {
        byCik = filings.GroupBy(f => f.Cik).ToDictionary(g => g.Key, g => g.OrderBy(f => f.PublicDay).ThenBy(f => f.AccessionNumber, StringComparer.Ordinal).ToList());
        facts = classified;
        Count = byCik.Values.Sum(l => l.Count);
    }

    public int Count { get; }

    public bool HasCik(int cik) => byCik.ContainsKey(cik);

    /// <summary>The feature values in <see cref="DilutionFeatures.All"/> order for a decision at <paramref name="cutoffEt"/> on day <paramref name="day"/>.</summary>
    public double[] Features(int cik, DateOnly day, DateTime cutoffEt)
    {
        var result = Enumerable.Repeat(double.NaN, DilutionFeatures.All.Length).ToArray();
        if (!byCik.TryGetValue(cik, out var all)) return result;
        var known = all.Where(f => f.KnownBefore(day, cutoffEt)).ToList();
        double DaysSince(Func<string, bool> form) => known.LastOrDefault(f => form(f.FormType)) is { } f ? day.DayNumber - f.PublicDay.DayNumber : double.NaN;

        // ATM: classified 424B5s and shelves inside the active window.
        var window = known.Where(f => (f.FormType == "424B5" || DilutionForms.IsShelf(f.FormType)) && f.PublicDay >= day.AddDays(-DilutionFeatures.AtmActiveDays)).ToList();
        var atm = window.Where(f => facts.TryGetValue(f.AccessionNumber, out var a) && a.IsAtm).ToList();
        bool unclassified424B5 = window.Any(f => f.FormType == "424B5" && !facts.ContainsKey(f.AccessionNumber));
        result[0] = atm.Count > 0 ? 1 : unclassified424B5 ? double.NaN : 0;
        var lastAtm = known.LastOrDefault(f => facts.TryGetValue(f.AccessionNumber, out var a) && a.IsAtm);
        result[1] = lastAtm is null ? double.NaN : day.DayNumber - lastAtm.PublicDay.DayNumber;
        result[2] = lastAtm is not null && facts[lastAtm.AccessionNumber].OfferingUsd is { } usd && usd > 0 ? Math.Log(usd) : double.NaN;

        // Baby shelf: the latest classified text that states the I.B.6 limit.
        var shelf = known.LastOrDefault(f => facts.TryGetValue(f.AccessionNumber, out var a) && a.BabyShelf);
        if (shelf is not null)
        {
            var a = facts[shelf.AccessionNumber];
            result[3] = 1;
            result[4] = a.HeadroomUsd is { } h && a.CapacityUsd is { } c && c > 0 ? h / c : double.NaN;
        }
        else if (known.Any(f => facts.ContainsKey(f.AccessionNumber))) result[3] = 0;

        result[5] = known.Count(f => DilutionForms.Is6K(f.FormType) && f.PublicDay >= day.AddDays(-30));
        var priorClose = PriorWeekday(day).ToDateTime(new TimeOnly(16, 0));
        result[6] = known.Any(f => DilutionForms.Is6K(f.FormType) && f.AcceptedEt is { } t && t >= priorClose) ? 1 : 0;
        result[7] = DaysSince(f => f == "EFFECT");
        result[8] = DaysSince(DilutionForms.IsS1Amendment);
        result[9] = DaysSince(DilutionForms.IsForeignRegistration);
        result[10] = DaysSince(DilutionForms.IsOther424B);
        result[11] = known.Count(f => DilutionForms.IsSc13G(f.FormType) && f.PublicDay >= day.AddDays(-5));
        return result;
    }

    /// <summary>The previous weekday (holidays are not skipped: a 6-K accepted after the earlier close still counts as after-hours news).</summary>
    private static DateOnly PriorWeekday(DateOnly day)
    {
        var d = day.AddDays(-1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }

    // ── files ────────────────────────────────────────────────────────────

    public const string FilingsFile = "filings.csv";
    public const string HeadersFile = "headers.csv";
    public const string ClassifiedFile = "classified.csv";
    public const string FilingsHeader = "cik,form,accession,filed";
    public const string HeadersHeader = "accession,acceptedEt";
    public const string ClassifiedHeader = "accession,isAtm,atmOnCover,offeringUsd,babyShelf,publicFloatUsd,soldPrior12mUsd,phrases";
    private const string EtFormat = "yyyy-MM-dd HH:mm:ss";

    public static string FilingLine(DilutionFiling f) => string.Create(CultureInfo.InvariantCulture, $"{f.Cik},{f.FormType},{f.AccessionNumber},{f.Filed:yyyy-MM-dd}");

    public static DilutionFiling? ParseFiling(string line)
    {
        var c = line.Split(',');
        if (c.Length != 4 || !int.TryParse(c[0], CultureInfo.InvariantCulture, out var cik) || !DateOnly.TryParseExact(c[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filed)) return null;
        return new DilutionFiling(cik, c[1], c[2], filed, null);
    }

    /// <summary>One answered header fetch: the acceptance time in ET to the second, empty when the header carried none.</summary>
    public static string HeaderLine(string accession, DateTime? acceptedEt) =>
        $"{accession},{(acceptedEt is { } a ? a.ToString(EtFormat, CultureInfo.InvariantCulture) : "")}";

    public static (string Accession, DateTime? AcceptedEt)? ParseHeader(string line)
    {
        var c = line.Split(',');
        if (c.Length != 2 || c[0].Length == 0) return null;
        return (c[0], DateTime.TryParseExact(c[1], EtFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) ? a : null);
    }

    public static string ClassifiedLine(string accession, AtmShelfFacts a)
    {
        static string N(double? v) => v is { } x ? x.ToString("0", CultureInfo.InvariantCulture) : "";
        return $"{accession},{(a.IsAtm ? 1 : 0)},{(a.AtmOnCover ? 1 : 0)},{N(a.OfferingUsd)},{(a.BabyShelf ? 1 : 0)},{N(a.PublicFloatUsd)},{N(a.SoldPrior12mUsd)},{string.Join(';', a.Phrases)}";
    }

    public static (string Accession, AtmShelfFacts Facts)? ParseClassified(string line)
    {
        var c = line.Split(',');
        if (c.Length != 8 || c[0].Length == 0) return null;
        static double? N(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return (c[0], new AtmShelfFacts(c[1] == "1", c[2] == "1", N(c[3]), c[4] == "1", N(c[5]), N(c[6]), c[7].Length == 0 ? [] : c[7].Split(';')));
    }

    /// <summary>Accessions with an answered header fetch and their acceptance time in ET (null when the header had none).</summary>
    public static Dictionary<string, DateTime?> ReadHeaders(string dir)
    {
        var result = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
        var file = Path.Combine(dir, HeadersFile);
        if (!File.Exists(file)) return result;
        foreach (var line in File.ReadLines(file).Skip(1))
            if (ParseHeader(line) is { } h && (h.AcceptedEt is not null || !result.ContainsKey(h.Accession))) result[h.Accession] = h.AcceptedEt;
        return result;
    }

    /// <summary>Index rows (one per CIK and accession) from filings.csv.</summary>
    public static List<DilutionFiling> ReadFilings(string dir)
    {
        var file = Path.Combine(dir, FilingsFile);
        if (!File.Exists(file)) return [];
        var seen = new HashSet<(int, string)>();
        var list = new List<DilutionFiling>();
        foreach (var line in File.ReadLines(file).Skip(1))
            if (ParseFiling(line) is { } f && seen.Add((f.Cik, f.AccessionNumber))) list.Add(f);
        return list;
    }

    public static Dictionary<string, AtmShelfFacts> ReadClassified(string dir)
    {
        var result = new Dictionary<string, AtmShelfFacts>(StringComparer.Ordinal);
        var file = Path.Combine(dir, ClassifiedFile);
        if (!File.Exists(file)) return result;
        foreach (var line in File.ReadLines(file).Skip(1))
            if (ParseClassified(line) is { } c) result[c.Accession] = c.Facts;
        return result;
    }

    /// <summary>filings.csv joined with headers.csv (acceptance times) and classified.csv (detector output); null without filings.csv.</summary>
    public static DilutionHistory? Load(string dir)
    {
        if (!File.Exists(Path.Combine(dir, FilingsFile))) return null;
        var headers = ReadHeaders(dir);
        var filings = ReadFilings(dir).Select(f => headers.TryGetValue(f.AccessionNumber, out var a) && a is not null ? f with { AcceptedEt = a } : f);
        return new DilutionHistory(filings, ReadClassified(dir));
    }
}
