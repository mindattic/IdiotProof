using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// What the classifier said about one item: the catalyst type and its non-binding-LOI flag, the rule that chose it,
/// and the frozen version 1 reading (<see cref="Legacy"/> class, <see cref="Direction"/> -1/0/+1, <see cref="Confidence"/>
/// 0..1, <see cref="LegacyRule"/>) that the catalyst score and the gapper study still use.
/// </summary>
public readonly record struct CatalystPreClassification(
    CatalystPreClass Class, bool NonBindingLoi, int Direction, double Confidence, string Rule, CatalystLegacyClass Legacy, string LegacyRule);

/// <summary>A catalyst type read from stored fields alone: the type, the LOI flag and the rule that chose it.</summary>
public readonly record struct CatalystTypeResult(CatalystPreClass Class, bool NonBindingLoi, string Rule);

/// <summary>
/// Deterministic, LLM-free catalyst classification (IP-T487). One item gets one <see cref="CatalystPreClass"/>:
/// a news headline through the keyword classifier fixed on the gapper study's train clusters
/// (<see cref="CatalystTypes"/>, with the version 1 class as a hint), a filing through its form type and 8-K items,
/// then one label by a fixed priority (gapper study amendment 08). The version 1 rule tables
/// (<see cref="CatalystLegacyClassifier"/>) run first and are returned beside the type, unchanged.
/// The same code runs live, in the backfills and over the stored history.
/// </summary>
public static class CatalystPreClassifier
{
    /// <summary>
    /// Which type wins when an item names several, most specific first. LOI wording stands in the contract slot: on its own
    /// it is a contract / partnership, and it flags whatever type wins.
    /// </summary>
    private static readonly (string Key, CatalystPreClass Type)[] Priority =
    [
        ("coverage", CatalystPreClass.NoCatalyst),
        ("rsplit", CatalystPreClass.ReverseSplit),
        ("acqTarget", CatalystPreClass.AcquisitionTarget),
        ("merger", CatalystPreClass.Merger),
        ("acqAcquirer", CatalystPreClass.AcquisitionAcquirer),
        ("fda", CatalystPreClass.FdaClinical),
        ("offering", CatalystPreClass.OfferingDilution),
        ("pipe", CatalystPreClass.OutsideInvestment),
        ("earnBeat", CatalystPreClass.EarningsBeat),
        ("earnMiss", CatalystPreClass.EarningsMiss),
        ("guidance", CatalystPreClass.Guidance),
        ("earnOther", CatalystPreClass.EarningsReport),
        ("uplist", CatalystPreClass.Uplisting),
        ("compliance", CatalystPreClass.ListingCompliance),
        ("buyback", CatalystPreClass.Buyback),
        ("contract", CatalystPreClass.ContractPartnership),
        ("loi", CatalystPreClass.ContractPartnership),
        ("upgrade", CatalystPreClass.AnalystUpgrade),
        ("theme", CatalystPreClass.ThemePivot),
    ];

    private static readonly int LoiBit = CatalystTypes.Bit("loi");

    public static CatalystPreClassification Classify(string? headline, string? summary, string? formType = null,
        IReadOnlyList<string>? itemCodes = null, string? source = null) =>
        Classify(CatalystCatalogs.PreClassRules, headline, summary, formType, itemCodes, source);

    public static CatalystPreClassification Classify(CatalystPreClassRules rules, string? headline, string? summary,
        string? formType = null, IReadOnlyList<string>? itemCodes = null, string? source = null)
    {
        var legacy = CatalystLegacyClassifier.Classify(rules, headline, summary, formType, itemCodes, source);
        var type = TypeOf(headline, formType, itemCodes, legacy.Class.ToString());
        return new(type.Class, type.NonBindingLoi, legacy.Direction, legacy.Confidence, type.Rule, legacy.Class, legacy.Rule);
    }

    /// <summary>
    /// The type of a stored item from the fields every store keeps: the headline and the version 1 class of a news item,
    /// or the form type and 8-K items of a filing (a filing's headline is its form and filer name, so it is not read).
    /// Pure; the reclassification of stored rows and the live intake both end here.
    /// </summary>
    public static CatalystTypeResult TypeOf(string? headline, string? formType, IReadOnlyList<string>? itemCodes, string? legacyClass)
    {
        if (string.IsNullOrWhiteSpace(formType))
        {
            int bits = CatalystTypes.FromHeadline(headline, legacyClass);
            var (type, loi) = FromBits(bits);
            return new(type, loi, bits == 0 ? "no type matched" : "headline: " + Keys(bits));
        }

        var form = formType.Trim().ToUpperInvariant();
        if (IsOwnershipForm(form)) return new(CatalystPreClass.NoCatalyst, false, $"form {form} (ownership report)");
        var items = (itemCodes ?? []).Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
        int filed = CatalystTypes.FromFiling(form, items);
        // The version 1 class adds what only its keyword rules can see in a filing's text: FDA, an uplisting, a reverse
        // split, and a merger when the form and items name no acquisition themselves.
        filed |= legacyClass switch
        {
            nameof(CatalystLegacyClass.Fda) => CatalystTypes.Bit("fda"),
            nameof(CatalystLegacyClass.Uplisting) => CatalystTypes.Bit("uplist"),
            nameof(CatalystLegacyClass.ReverseSplit) => CatalystTypes.Bit("rsplit"),
            nameof(CatalystLegacyClass.MA) when (filed & (CatalystTypes.Bit("acqTarget") | CatalystTypes.Bit("acqAcquirer") | CatalystTypes.Bit("merger"))) == 0 => CatalystTypes.Bit("merger"),
            _ => 0,
        };
        var (filedType, filedLoi) = FromBits(filed);
        var label = items.Count == 0 ? $"form {form}" : $"form {form} items {string.Join(",", items)}";
        return new(filedType, filedLoi, filed == 0 ? $"{label}: no mapped event" : $"{label}: {Keys(filed)}");
    }

    /// <summary>One type from a <see cref="CatalystTypes"/> bit set by <see cref="Priority"/>; no bit = other. The LOI flag is the LOI bit.</summary>
    public static (CatalystPreClass Type, bool NonBindingLoi) FromBits(int bits)
    {
        bool loi = (bits & LoiBit) != 0;
        foreach (var (key, type) in Priority)
            if ((bits & CatalystTypes.Bit(key)) != 0) return (type, loi);
        return (CatalystPreClass.Other, loi);
    }

    /// <summary>Insider and institutional ownership reports: filed on a schedule or after a trade, not an event at the company.</summary>
    public static bool IsOwnershipForm(string form) =>
        form is "3" or "3/A" or "4" or "4/A" or "5" or "5/A" or "144" or "144/A"
        || form.StartsWith("13F", StringComparison.Ordinal)
        || form.StartsWith("SC 13G", StringComparison.Ordinal) || form.StartsWith("SCHEDULE 13G", StringComparison.Ordinal);

    /// <summary>1 primary (EDGAR), 2 editorial, 3 wire/promotional, per the catalog's source table.</summary>
    public static int SourceTier(CatalystPreClassRules rules, string? sourceName, bool isEdgar) =>
        CatalystLegacyClassifier.SourceTier(rules, sourceName, isEdgar);

    public static bool IsWire(CatalystPreClassRules rules, string? sourceName) => CatalystLegacyClassifier.IsWire(rules, sourceName);

    private static string Keys(int bits) => string.Join("+", CatalystTypes.Keys.Where(k => (bits & CatalystTypes.Bit(k)) != 0));
}
