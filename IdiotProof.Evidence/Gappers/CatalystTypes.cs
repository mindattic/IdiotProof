using System.Text.RegularExpressions;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// A transparent keyword plus 8-K-item catalyst classifier (IP-T484, explore watchlist): each news headline (with its
/// deterministic pre-class) or EDGAR filing (form type and 8-K items) maps to a bit set over <see cref="Keys"/>. Several
/// types can fire on one item. Regex only, no model; meant for lift tables, not for trading. Pure and deterministic, so live
/// code and backtests share it.
/// <para>
/// IP-T488 fixes from the NMF topic clusters of the train news (docs: runs/explore/catalyst-ml-*.md, section 6): templated
/// coverage (movers lists, market wraps, halt notices, option alerts, short-interest notes, earnings calendars and previews,
/// analyst roundups) now gets only the <c>coverage</c> bit, since its keywords name other stocks or describe the move rather than
/// an event at the company; PIPE is case-sensitive ("steel pipe", "Pipelines") and a fund's "stake in" no longer counts; a
/// self-tender or exchange offer for the company's own shares is a buyback, not a target; earnings take the first verdict
/// (EPS before sales) instead of tagging "EPS beats, sales miss" as both; FDA needs a clinical context (a company called
/// "Ortho Clinical" or a court trial is not one) and IND / NDA / BLA / EMA are case-sensitive; "offering" needs a financing
/// context (not a product offering) and ATM / AI are case-sensitive (bank ATMs, "BigBear.ai"); "wins" needs a contract word;
/// Nasdaq continued-listing, deficiency and regained-compliance news is <c>compliance</c>, not an uplisting; and 8-K items
/// 3.01 (listing-rule notice) and 5.03 (charter amendment, usually the reverse split) are mapped. New keys are appended so
/// the older bit positions stay put.
/// </para>
/// </summary>
public static class CatalystTypes
{
    public static readonly string[] Keys =
        ["pipe", "acqTarget", "acqAcquirer", "merger", "earnBeat", "earnMiss", "earnOther", "guidance", "contract", "loi", "fda", "rsplit", "uplist", "buyback", "upgrade", "offering", "theme",
         "compliance", "coverage"];

    public static readonly string[] Names =
    [
        "outside investment / PIPE", "acquisition (target)", "acquisition (acquirer)", "merger", "earnings beat", "earnings miss", "earnings (no beat/miss word)",
        "guidance change", "contract or partnership", "non-binding LOI / MOU", "FDA or clinical", "reverse split / ADS ratio change", "uplisting", "buyback",
        "analyst upgrade", "offering or dilution", "theme pivot", "listing compliance / deficiency notice", "coverage of a move or the market (not a catalyst)",
    ];

    public const string NoCatalyst = "no catalyst";

    public static int Bit(string key) => 1 << Array.IndexOf(Keys, key);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const RegexOptions Cased = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>Benzinga-style templates about the tape, the market or analysts, not about an event at the company.</summary>
    private static readonly Regex Coverage = new(
        @"stocks moving (in|premarket|after)|here are \d+ (other )?stocks|other stocks moving|\d+ biggest movers|biggest movers from|stocks to watch|gainers and losers|stock movers for|" +
        @"earnings scheduled for|earnings preview|earnings outlook|earnings insights|^recap:|\bpreview:|option alert|whale (alerts|activity)|short interest|short volatility alert|" +
        @"analyst ratings (for|have)|price target changes|top ratings upgrades|benzinga before the bell|bulls and bears|opening bell update|market update|" +
        @"^(us|u\.s\.) stocks|\b(dow|nasdaq|s&p 500)\b (jumps|rises|falls|surges|dips|drops|gains|tumbles|slides|edges|climbs|sinks|turns|up|down)\b|" +
        @"halted on circuit breaker|shares resume trad|52-week (lows|highs)|how much \$\d+ invested|value stocks|week in cannabis|biggest ev stories|trending stocks on", Opt);

    private static readonly Regex Pipe = new(@"private placement|strategic investment|equity investment|\binvest(s|ment of)? \$|securities purchase agreement|investment from|\d+(\.\d+)?% stake", Opt);
    private static readonly Regex PipeWord = new(@"\bPIPE\b", Cased);
    private static readonly Regex Target = new(@"to be acquired|agrees? to be acquired|acquired by|take[- ]private|go[- ]private|tender offer|buyout|(proposal|offer) to acquire|unsolicited (proposal|offer)|to be taken private", Opt);
    /// <summary>A tender or exchange offer for the company's own shares: a buyback.</summary>
    private static readonly Regex SelfTender = new(@"dutch auction|tender offer to (re)?purchase|(tender|exchange) offer .{0,40}\b(its|own)\b .{0,20}(shares|stock)|offer to acquire shares of its", Opt);
    private static readonly Regex Acquirer = new(@"\bto acquire\b|\bacquires\b|\bacquiring\b|completes? (the )?acquisition|acquisition of|agreement to acquire", Opt);
    private static readonly Regex Merger = new(@"\bmerger\b|\bmerge with\b|business combination|\bSPAC\b|reverse merger", Opt);
    private static readonly Regex EarnCtx = new(@"\bearnings\b|\bEPS\b|\bFFO\b|\brevenue|\bresults\b|\bQ[1-4]\b|quarter|fiscal|full[- ]year", Opt);
    /// <summary>The first verdict word decides (EPS comes before sales in the wire format).</summary>
    private static readonly Regex Verdict = new(@"\b(beats?|tops?|exceeds?|surpass\w*|better[- ]than[- ]expected|misses|miss|falls? short|below (estimates|expectations)|worse[- ]than[- ]expected|in[- ]?line)\b", Opt);
    private static readonly Regex Beat = new(@"record (revenue|quarter|results)", Opt);
    private static readonly Regex Guidance = new(@"(rais|lower|cut|reaffirm|boost|increas|reduc|withdraw|updat)\w* .{0,30}(guidance|outlook|forecast)|(guidance|outlook|forecast) .{0,20}(rais|lower|cut|boost|increas|reduc)", Opt);
    private static readonly Regex Contract = new(@"\bcontract\b|partnership|partners? with|collaborat|agreement with|supply agreement|license agreement|licensing|purchase order|\border(s)? (from|for|worth)\b|\bawarded\b|\bwins\b.{0,40}(contract|order|award|deal|tender|bid)|teams? (up )?with|selected by|distribution agreement|joint venture|strategic alliance", Opt);
    private static readonly Regex Loi = new(@"letter of intent|\bLOI\b|memorandum of understanding|\bMOU\b|non-binding|term sheet", Opt);
    private static readonly Regex Fda = new(@"\bFDA\b|clinical (trial|study|hold|data|results|program|stage)|\bphase (1|2|3|i|ii|iii)[ab]?\b|topline|primary endpoint|orphan drug|fast track|breakthrough therapy|510\(k\)|pivotal (study|trial)", Opt);
    private static readonly Regex FdaCased = new(@"\b(IND|NDA|sNDA|BLA|EMA|PDUFA)\b", Cased);
    private static readonly Regex RSplit = new(@"reverse (stock |share )?split|share consolidation|ADS ratio|ratio change|change (in|to) (the )?(ADS|ADR)", Opt);
    private static readonly Regex Uplist = new(@"up-?list|approved (for|to) list|(begin|commence)s? trading on (the )?(nasdaq|nyse)|transfers? (its )?listing to (the )?(nasdaq|nyse)", Opt);
    private static readonly Regex Compliance = new(@"continued listing|regain(s|ed)? compliance|minimum bid|bid price (requirement|deficiency|rule)|deficiency (notice|letter)|notice of (delisting|non-?compliance)|delisting (notice|determination)|hearings? panel|panel grants|listing (rule|requirement|standard)", Opt);
    private static readonly Regex Buyback = new(@"buy-?back|repurchase", Opt);
    private static readonly Regex Upgrade = new(@"\bupgrade[sd]?\b|initiates? coverage|raises? (its )?price target|price target (raised|increased|boosted)|outperform rating|overweight rating", Opt);
    private static readonly Regex Offering = new(@"(public|registered direct|secondary|underwritten|follow-on|best[- ]efforts|equity|stock|share|unit|debt|notes|convertible|at-the-market|ADS) offering|offering of \$?[\d.,]+|offering of (common|ordinary|shares|units|ADS)|registered direct|at-the-market|warrant (exercise|inducement)|prices? .{0,40}(shares|units)|\bpriced (at|above|below)|shelf registration|mixed shelf|dilution|convertible note", Opt);
    private static readonly Regex OfferingCased = new(@"\bATM\b", Cased);
    private static readonly Regex Theme = new(@"artificial intelligence|bitcoin|\bcrypto|blockchain|digital asset|treasury (strategy|reserve)|\btokens?\b|ethereum|solana|quantum|nuclear|\bdrones?\b|rebrand|name change|\bpivot|stablecoin|data center|\bGPU", Opt);
    private static readonly Regex ThemeCased = new(@"(?<![.\w])AI\b", Cased);
    private static readonly Regex EarnOther = new(@"\b(reports?|announces?|posts?)\b.{0,60}(results|earnings)|\bearnings\b", Opt);

    /// <summary>The types a news headline names; <paramref name="preClass"/> (the rule tables' pre-class) adds its own type.</summary>
    public static int FromHeadline(string? headline, string? preClass)
    {
        string h = headline ?? "";
        if (Coverage.IsMatch(h)) return Bit("coverage");
        int bits = 0;
        void Set(string key) => bits |= Bit(key);
        bool selfTender = SelfTender.IsMatch(h);
        bool target = !selfTender && Target.IsMatch(h);
        if (Pipe.IsMatch(h) || PipeWord.IsMatch(h)) Set("pipe");
        if (target) Set("acqTarget");
        else if (!selfTender && Acquirer.IsMatch(h)) Set("acqAcquirer");
        if (selfTender) Set("buyback");
        if (Merger.IsMatch(h)) Set("merger");
        bool earnCtx = EarnCtx.IsMatch(h) || preClass == "Earnings";
        if (earnCtx)
        {
            var v = Verdict.Match(h);
            if (v.Success)
            {
                var w = v.Value.ToLowerInvariant();
                if (w.StartsWith("miss", StringComparison.Ordinal) || w.StartsWith("fall", StringComparison.Ordinal) || w.StartsWith("below", StringComparison.Ordinal) || w.StartsWith("worse", StringComparison.Ordinal)) Set("earnMiss");
                else if (!w.StartsWith("in", StringComparison.Ordinal)) Set("earnBeat");
            }
            else if (Beat.IsMatch(h)) Set("earnBeat");
        }
        if (Guidance.IsMatch(h)) Set("guidance");
        if ((preClass == "Earnings" || (earnCtx && EarnOther.IsMatch(h))) && (bits & (Bit("earnBeat") | Bit("earnMiss"))) == 0) Set("earnOther");
        bool loi = Loi.IsMatch(h);
        if (loi) Set("loi");
        else if (Contract.IsMatch(h) || preClass == "ContractAward") Set("contract");
        if (Fda.IsMatch(h) || FdaCased.IsMatch(h) || preClass == "Fda") Set("fda");
        if (RSplit.IsMatch(h) || preClass == "ReverseSplit") Set("rsplit");
        bool compliance = Compliance.IsMatch(h);
        if (compliance) Set("compliance");
        else if (Uplist.IsMatch(h) || preClass == "Uplisting") Set("uplist");
        if (Buyback.IsMatch(h)) Set("buyback");
        if (Upgrade.IsMatch(h)) Set("upgrade");
        if (Offering.IsMatch(h) || OfferingCased.IsMatch(h) || preClass == "OfferingDilution") Set("offering");
        if (Theme.IsMatch(h) || ThemeCased.IsMatch(h)) Set("theme");
        if (preClass == "MA" && (bits & (Bit("acqTarget") | Bit("acqAcquirer") | Bit("merger"))) == 0) Set("merger");
        return bits;
    }

    /// <summary>
    /// The types an EDGAR filing implies: registration statements and prospectuses = offering; SC 13D = outside investment;
    /// 8-K items 2.02 earnings, 3.02 unregistered equity sale (PIPE), 1.01 material agreement (contract), 2.01 completed
    /// acquisition (acquirer), 3.01 listing-rule notice (compliance), 3.03 modified holder rights and 5.03 charter amendment
    /// (usually a reverse split). Other items imply nothing.
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
                    "3.01" => Bit("compliance"),
                    "3.03" => Bit("rsplit"),
                    "5.03" => Bit("rsplit"),
                    _ => 0,
                };
        return bits;
    }
}

/// <summary>
/// IP-T488: the catalyst clusters found by NMF topics on the train news (tools/gapper-catalyst-ml, names.json) and the EDGAR
/// form / 8-K item classes, mapped to the <see cref="CatalystTypes"/> keys they stand for. Used to check the keyword classifier
/// against the clusters (a tag outside its mapped clusters is a false-positive proxy) and to read a cluster label as a type.
/// A residual cluster ("other company news", the "why shares are moving" explainers, 8-K 7.01 / 8.01) can carry any type.
/// Pure.
/// </summary>
public static class CatalystClusterMap
{
    private static readonly IReadOnlyList<string> Any = CatalystTypes.Keys.Where(k => k != "coverage").ToList();

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["earnings miss (EPS/sales vs estimate)"] = ["earnMiss", "earnOther"],
        ["earnings beat (EPS/sales vs estimate)"] = ["earnBeat", "earnOther"],
        ["earnings scorecard, EPS only (mostly beats of a loss estimate)"] = ["earnBeat", "earnMiss", "earnOther"],
        ["earnings scorecard vs year ago"] = ["earnBeat", "earnMiss", "earnOther"],
        ["guidance vs estimate"] = ["guidance", "earnOther", "earnBeat", "earnMiss"],
        ["earnings preview / insights recap"] = ["coverage"],
        ["post-results move explainer"] = ["earnBeat", "earnMiss", "earnOther", "guidance"],
        ["movers list (sector, session)"] = ["coverage"],
        ["why-shares-are-moving explainer"] = Any,
        ["analyst price target / rating change"] = ["upgrade"],
        ["analyst initiation"] = ["upgrade"],
        ["analyst ratings roundup"] = ["upgrade", "coverage"],
        ["volatility halt / trading resumed"] = ["coverage"],
        ["unusual options activity (sweeps, whale alerts)"] = ["coverage"],
        ["market wrap"] = ["coverage"],
        ["stocks-to-watch / evergreen commentary"] = ["coverage"],
        ["short interest report"] = ["coverage"],
        ["underwritten public offering / resale registration"] = ["offering"],
        ["registered direct offering / private placement (PIPE)"] = ["offering", "pipe"],
        ["clinical trial update / data"] = ["fda"],
        ["FDA / regulatory approval, clearance, designation"] = ["fda"],
        ["acquisition, terms not disclosed"] = ["acqAcquirer"],
        ["acquisition agreement (definitive, priced)"] = ["acqAcquirer", "acqTarget", "merger", "loi"],
        ["insider purchase (Form 4) / 13D proposal"] = ["pipe", "acqTarget"],
        ["executive appointment"] = [],
        ["share repurchase program"] = ["buyback"],
        ["commercial / distribution / purchase agreement"] = ["contract", "loi"],
        ["strategic partnership"] = ["contract", "loi"],
        ["contract award"] = ["contract"],
        ["licensing / collaboration deal"] = ["contract", "fda"],
        ["AI product / partnership"] = ["theme", "contract"],
        ["crypto / bitcoin miners"] = ["theme"],
        ["sector news: EV and autos"] = [],
        ["sector news: cannabis"] = [],
        ["sector news: clean energy and hydrogen"] = ["contract", "loi"],
        ["product launch / operations update"] = [],
        ["other company news"] = Any,
        ["8-K 1.01 material agreement"] = ["contract", "loi", "pipe", "acqAcquirer", "acqTarget", "merger", "offering"],
        ["8-K 2.01 completed acquisition/disposition"] = ["acqAcquirer"],
        ["8-K 1.02 agreement terminated"] = [],
        ["8-K 2.02 results of operations"] = ["earnOther", "earnBeat", "earnMiss", "guidance"],
        ["8-K 3.01 delisting / listing-rule notice"] = ["compliance"],
        ["8-K 3.02 unregistered equity sale"] = ["pipe"],
        ["8-K 3.03 holder rights modified (reverse split)"] = ["rsplit"],
        ["8-K 5.03 charter / bylaw amendment"] = ["rsplit"],
        ["8-K 5.07 shareholder vote"] = ["rsplit"],
        ["8-K 5.02 officer / director change"] = [],
        ["8-K 7.01 Reg FD"] = Any,
        ["8-K 8.01 other events"] = Any,
        ["8-K other items"] = Any,
        ["8-K/A amendment"] = Any,
        ["S-1/F-1 registration"] = ["offering"],
        ["S-3/F-3 shelf"] = ["offering"],
        ["424B4 priced offering"] = ["offering"],
        ["424B5 shelf takedown"] = ["offering"],
        ["424B other prospectus"] = ["offering"],
        ["SC 13D stake"] = ["pipe"],
        ["SC 13G passive stake"] = [],
        ["Form 25 delisting"] = ["compliance"],
        ["Form 15 deregistration"] = ["compliance"],
    };

    /// <summary>The types a cluster stands for; an unknown cluster name maps to none.</summary>
    public static IReadOnlyList<string> TypesOf(string clusterName) => Map.TryGetValue(clusterName, out var l) ? l : [];
}
