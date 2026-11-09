using System.Text.RegularExpressions;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// A transparent keyword plus 8-K-item catalyst classifier (IP-T484, explore watchlist): each news headline (with its
/// deterministic pre-class) or EDGAR filing (form type and 8-K items) maps to a bit set over <see cref="Keys"/>. Several
/// types can fire on one item. Regex only, no model; meant for lift tables, not for trading. Pure.
/// </summary>
public static class CatalystTypes
{
    public static readonly string[] Keys =
        ["pipe", "acqTarget", "acqAcquirer", "merger", "earnBeat", "earnMiss", "earnOther", "guidance", "contract", "loi", "fda", "rsplit", "uplist", "buyback", "upgrade", "offering", "theme"];

    public static readonly string[] Names =
    [
        "outside investment / PIPE", "acquisition (target)", "acquisition (acquirer)", "merger", "earnings beat", "earnings miss", "earnings (no beat/miss word)",
        "guidance change", "contract or partnership", "non-binding LOI / MOU", "FDA or clinical", "reverse split / ADS ratio change", "uplisting", "buyback",
        "analyst upgrade", "offering or dilution", "theme pivot",
    ];

    public const string NoCatalyst = "no catalyst";

    public static int Bit(string key) => 1 << Array.IndexOf(Keys, key);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly Regex Pipe = new(@"private placement|\bPIPE\b|strategic investment|equity investment|\binvest(s|ment of)? \$|securities purchase agreement|investment from|\bstake in\b", Opt);
    private static readonly Regex Target = new(@"to be acquired|agrees? to be acquired|acquired by|take[- ]private|go[- ]private|tender offer|buyout|(proposal|offer) to acquire|unsolicited (proposal|offer)|to be taken private", Opt);
    private static readonly Regex Acquirer = new(@"\bto acquire\b|\bacquires\b|\bacquiring\b|completes? (the )?acquisition|acquisition of|agreement to acquire", Opt);
    private static readonly Regex Merger = new(@"\bmerger\b|\bmerge with\b|business combination|\bSPAC\b|reverse merger", Opt);
    private static readonly Regex EarnCtx = new(@"\bearnings\b|\bEPS\b|\brevenue|\bresults\b|\bQ[1-4]\b|quarter|fiscal|full[- ]year", Opt);
    private static readonly Regex Beat = new(@"\bbeats?\b|\btops?\b|\bexceeds?\b|surpass|better[- ]than[- ]expected|record (revenue|quarter|results)", Opt);
    private static readonly Regex Miss = new(@"\bmiss(es)?\b|falls? short|below (estimates|expectations)|worse[- ]than[- ]expected", Opt);
    private static readonly Regex Guidance = new(@"(rais|lower|cut|reaffirm|boost|increas|reduc|withdraw|updat)\w* .{0,30}(guidance|outlook|forecast)|(guidance|outlook|forecast) .{0,20}(rais|lower|cut|boost|increas|reduc)", Opt);
    private static readonly Regex Contract = new(@"\bcontract\b|partnership|partners? with|collaborat|agreement with|supply agreement|license agreement|licensing|purchase order|\border(s)? (from|for|worth)\b|\bawarded\b|\bwins\b|teams? (up )?with|selected by|distribution agreement|joint venture|strategic alliance", Opt);
    private static readonly Regex Loi = new(@"letter of intent|\bLOI\b|memorandum of understanding|\bMOU\b|non-binding|term sheet", Opt);
    private static readonly Regex Fda = new(@"\bFDA\b|clinical|\bphase (1|2|3|i|ii|iii)\b|\btrial\b|topline|orphan drug|fast track|breakthrough therapy|510\(k\)|\bIND\b|\bNDA\b|\bBLA\b|\bEMA\b", Opt);
    private static readonly Regex RSplit = new(@"reverse (stock |share )?split|share consolidation|ADS ratio|ratio change|change (in|to) (the )?(ADS|ADR)", Opt);
    private static readonly Regex Uplist = new(@"up-?list|approved (for|to) list|(begin|commence)s? trading on (the )?(nasdaq|nyse)|listing on (the )?(nasdaq|nyse)", Opt);
    private static readonly Regex Buyback = new(@"buy-?back|repurchase", Opt);
    private static readonly Regex Upgrade = new(@"\bupgrade[sd]?\b|initiates? coverage|raises? (its )?price target|price target (raised|increased|boosted)|outperform rating|overweight rating", Opt);
    private static readonly Regex Offering = new(@"\boffering\b|registered direct|at-the-market|\bATM\b|warrant (exercise|inducement)|\bpriced\b|prices? .{0,40}shares|shelf registration|dilution|convertible note", Opt);
    private static readonly Regex Theme = new(@"\bAI\b|artificial intelligence|bitcoin|\bcrypto|blockchain|digital asset|treasury (strategy|reserve)|\btokens?\b|ethereum|solana|quantum|nuclear|\bdrones?\b|rebrand|name change|\bpivot|stablecoin|data center|\bGPU", Opt);

    /// <summary>The types a news headline names; <paramref name="preClass"/> (the rule tables' pre-class) adds its own type.</summary>
    public static int FromHeadline(string? headline, string? preClass)
    {
        string h = headline ?? "";
        int bits = 0;
        void Set(string key) => bits |= Bit(key);
        bool target = Target.IsMatch(h);
        if (Pipe.IsMatch(h)) Set("pipe");
        if (target) Set("acqTarget");
        else if (Acquirer.IsMatch(h)) Set("acqAcquirer");
        if (Merger.IsMatch(h)) Set("merger");
        bool earnCtx = EarnCtx.IsMatch(h) || preClass == "Earnings";
        if (earnCtx && Beat.IsMatch(h)) Set("earnBeat");
        if (earnCtx && Miss.IsMatch(h)) Set("earnMiss");
        if (Guidance.IsMatch(h)) Set("guidance");
        if ((preClass == "Earnings" || (earnCtx && Regex.IsMatch(h, @"\b(reports?|announces?|posts?)\b.{0,60}(results|earnings)|\bearnings\b", RegexOptions.IgnoreCase)))
            && (bits & (Bit("earnBeat") | Bit("earnMiss"))) == 0) Set("earnOther");
        bool loi = Loi.IsMatch(h);
        if (loi) Set("loi");
        else if (Contract.IsMatch(h) || preClass == "ContractAward") Set("contract");
        if (Fda.IsMatch(h) || preClass == "Fda") Set("fda");
        if (RSplit.IsMatch(h) || preClass == "ReverseSplit") Set("rsplit");
        if (Uplist.IsMatch(h) || preClass == "Uplisting") Set("uplist");
        if (Buyback.IsMatch(h)) Set("buyback");
        if (Upgrade.IsMatch(h)) Set("upgrade");
        if (Offering.IsMatch(h) || preClass == "OfferingDilution") Set("offering");
        if (Theme.IsMatch(h)) Set("theme");
        if (preClass == "MA" && (bits & (Bit("acqTarget") | Bit("acqAcquirer") | Bit("merger"))) == 0) Set("merger");
        return bits;
    }

    /// <summary>
    /// The types an EDGAR filing implies: registration statements and prospectuses = offering; SC 13D = outside investment;
    /// 8-K items 2.02 earnings, 3.02 unregistered equity sale (PIPE), 1.01 material agreement (contract), 2.01 completed
    /// acquisition (acquirer), 3.03 modified holder rights (often a reverse split). Other items imply nothing.
    /// </summary>
    public static int FromFiling(string formType, IEnumerable<string> items)
    {
        int bits = 0;
        var f = formType.ToUpperInvariant();
        if (f.StartsWith("S-1", StringComparison.Ordinal) || f.StartsWith("S-3", StringComparison.Ordinal) || f.StartsWith("F-1", StringComparison.Ordinal)
            || f.StartsWith("F-3", StringComparison.Ordinal) || f.StartsWith("424B", StringComparison.Ordinal)) bits |= Bit("offering");
        if (f.StartsWith("SC 13D", StringComparison.Ordinal)) bits |= Bit("pipe");
        if (f.StartsWith("8-K", StringComparison.Ordinal))
            foreach (var it in items)
                bits |= it.Trim() switch
                {
                    "2.02" => Bit("earnOther"),
                    "3.02" => Bit("pipe"),
                    "1.01" => Bit("contract"),
                    "2.01" => Bit("acqAcquirer"),
                    "3.03" => Bit("rsplit"),
                    _ => 0,
                };
        return bits;
    }
}
