namespace IdiotProof.Core.Catalysts;

/// <summary>
/// The catalyst type the deterministic classifier assigns to a news item or filing (IP-T487): what kind of event
/// moved, or could move, the stock. Stored by name on <see cref="Data.CatalystEvent.CatalystType"/> and on the
/// permanent history's news and filings. A non-binding LOI is a flag beside the type, not a type.
/// <para>
/// <see cref="Other"/> is company news of no listed type; <see cref="NoCatalyst"/> is an item with no event at the
/// company behind it (coverage of the move or the market, an insider ownership form).
/// </para>
/// <para>
/// The gapper study's frozen classes live on as <see cref="CatalystLegacyClass"/> (gapper study amendment 08).
/// </para>
/// </summary>
public enum CatalystPreClass
{
    OutsideInvestment,
    AcquisitionTarget,
    AcquisitionAcquirer,
    Merger,
    EarningsBeat,
    EarningsMiss,
    EarningsReport,
    Guidance,
    ContractPartnership,
    FdaClinical,
    ReverseSplit,
    Uplisting,
    ListingCompliance,
    Buyback,
    AnalystUpgrade,
    OfferingDilution,
    ThemePivot,
    Other,
    NoCatalyst,
}

/// <summary>
/// The coarse version 1 classes of the rule tables (<c>catalyst-preclass.json</c>, IP-T443), frozen with the gapper
/// study (protocol.md, params.json <c>types</c>): H2, H16, the news ride and the catalyst score read them, so they
/// are never renamed or reassigned. Stored by name on <see cref="Data.CatalystEvent.PreClass"/>.
/// </summary>
public enum CatalystLegacyClass
{
    Fda,
    ContractAward,
    ReverseSplit,
    OfferingDilution,
    Uplisting,
    Earnings,
    MA,
    PromoPr,
    Other,
}

/// <summary>Display names of the catalyst types, and the reading of a stored name.</summary>
public static class CatalystPreClassNames
{
    public static string Label(CatalystPreClass type) => type switch
    {
        CatalystPreClass.OutsideInvestment => "outside investment / PIPE",
        CatalystPreClass.AcquisitionTarget => "acquisition (target)",
        CatalystPreClass.AcquisitionAcquirer => "acquisition (acquirer)",
        CatalystPreClass.Merger => "merger",
        CatalystPreClass.EarningsBeat => "earnings beat",
        CatalystPreClass.EarningsMiss => "earnings miss",
        CatalystPreClass.EarningsReport => "earnings report (no beat/miss word)",
        CatalystPreClass.Guidance => "guidance change",
        CatalystPreClass.ContractPartnership => "contract / partnership",
        CatalystPreClass.FdaClinical => "FDA / clinical",
        CatalystPreClass.ReverseSplit => "reverse split / ADS ratio change",
        CatalystPreClass.Uplisting => "uplisting",
        CatalystPreClass.ListingCompliance => "listing compliance / deficiency",
        CatalystPreClass.Buyback => "buyback",
        CatalystPreClass.AnalystUpgrade => "analyst upgrade",
        CatalystPreClass.OfferingDilution => "offering / dilution",
        CatalystPreClass.ThemePivot => "theme pivot",
        CatalystPreClass.NoCatalyst => "no catalyst",
        _ => "other company news",
    };

    /// <summary>
    /// The type a stored name stands for. A row written before IP-T487 has no type, and one written by an older
    /// build may carry a version 1 class name: those read as the nearest type (Fda = FDA / clinical, ContractAward =
    /// contract / partnership, MA = merger, Earnings = earnings report, PromoPr and Other = other). Null or unknown = null.
    /// </summary>
    public static CatalystPreClass? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (Enum.TryParse<CatalystPreClass>(stored.Trim(), ignoreCase: true, out var type) && Enum.IsDefined(type)) return type;
        return Enum.TryParse<CatalystLegacyClass>(stored.Trim(), ignoreCase: true, out var legacy) && Enum.IsDefined(legacy) ? FromLegacy(legacy) : null;
    }

    /// <summary>The nearest type of a version 1 class, used when nothing finer is known about the item.</summary>
    public static CatalystPreClass FromLegacy(CatalystLegacyClass legacy) => legacy switch
    {
        CatalystLegacyClass.Fda => CatalystPreClass.FdaClinical,
        CatalystLegacyClass.ContractAward => CatalystPreClass.ContractPartnership,
        CatalystLegacyClass.ReverseSplit => CatalystPreClass.ReverseSplit,
        CatalystLegacyClass.OfferingDilution => CatalystPreClass.OfferingDilution,
        CatalystLegacyClass.Uplisting => CatalystPreClass.Uplisting,
        CatalystLegacyClass.Earnings => CatalystPreClass.EarningsReport,
        CatalystLegacyClass.MA => CatalystPreClass.Merger,
        _ => CatalystPreClass.Other,
    };
}
