namespace IdiotProof.Core.Catalysts;

/// <summary>
/// The deterministic catalyst class the rule tables (<c>catalyst-preclass.json</c>) assign before
/// any LLM sees the item. Stored by name on <see cref="Data.CatalystEvent.PreClass"/>.
/// </summary>
public enum CatalystPreClass
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
