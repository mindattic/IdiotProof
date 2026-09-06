using IdiotProof.Models;

namespace IdiotProof.Shared.Options;

/// <summary>
/// The "no mental math" numbers for a single option contract, decomposed from a LIVE
/// premium (no pricing model needed):
/// <list type="bullet">
///   <item><b>Intrinsic</b> — what the contract is worth if exercised right now ("real" value).</item>
///   <item><b>Extrinsic</b> — premium minus intrinsic: time value / hype. This is the number the
///   sell-into-strength thesis revolves around — it's what evaporates when reality arrives.</item>
///   <item><b>Breakeven</b> — the underlying price at expiration where the trade nets zero.
///   Only matters if you HOLD to expiration; selling early cashes in extrinsic instead.</item>
/// </list>
/// All prices are per-share; multiply by <see cref="OptionContract.Multiplier"/> for dollars per contract.
/// </summary>
public static class IntrinsicValueCalculator
{
    public static decimal Intrinsic(OptionRight right, decimal underlyingPrice, decimal strike) =>
        right == OptionRight.Call
            ? Math.Max(0m, underlyingPrice - strike)
            : Math.Max(0m, strike - underlyingPrice);

    /// <summary>Premium − intrinsic, floored at zero (a quote can print below parity for a moment).</summary>
    public static decimal Extrinsic(decimal premium, decimal intrinsic) => Math.Max(0m, premium - intrinsic);

    /// <summary>Share of the premium that is pure time value / hype, 0–100. Zero premium → 0.</summary>
    public static decimal ExtrinsicPercent(decimal premium, decimal intrinsic) =>
        premium <= 0m ? 0m : Math.Round(Extrinsic(premium, intrinsic) / premium * 100m, 1);

    public static decimal Breakeven(OptionRight right, decimal strike, decimal premium) =>
        right == OptionRight.Call ? strike + premium : strike - premium;

    /// <summary>Calendar days until expiration; 0 on expiration day; negative once expired.</summary>
    public static int DaysToExpiration(DateOnly expiration, DateOnly today) => expiration.DayNumber - today.DayNumber;

    /// <summary>Moneyness label for display: ITM / ATM / OTM. ATM within ±0.5% of strike.</summary>
    public static string Moneyness(OptionRight right, decimal underlyingPrice, decimal strike)
    {
        if (strike <= 0m) return "—";
        var pct = (underlyingPrice - strike) / strike;
        if (Math.Abs(pct) <= 0.005m) return "ATM";
        var inTheMoney = right == OptionRight.Call ? pct > 0m : pct < 0m;
        return inTheMoney ? "ITM" : "OTM";
    }

    /// <summary>One-call convenience bundle for a chain row / order ticket.</summary>
    public static OptionValueBreakdown Breakdown(OptionContract contract, decimal underlyingPrice, decimal premium, DateOnly today)
    {
        var intrinsic = Intrinsic(contract.Right, underlyingPrice, contract.Strike);
        return new OptionValueBreakdown(
            Premium: premium,
            Intrinsic: intrinsic,
            Extrinsic: Extrinsic(premium, intrinsic),
            ExtrinsicPercent: ExtrinsicPercent(premium, intrinsic),
            Breakeven: Breakeven(contract.Right, contract.Strike, premium),
            DaysToExpiration: DaysToExpiration(contract.Expiration, today),
            Moneyness: Moneyness(contract.Right, underlyingPrice, contract.Strike),
            CostPerContract: premium * contract.Multiplier);
    }
}

/// <summary>Per-share values unless noted. <see cref="CostPerContract"/> is dollars.</summary>
public sealed record OptionValueBreakdown(
    decimal Premium,
    decimal Intrinsic,
    decimal Extrinsic,
    decimal ExtrinsicPercent,
    decimal Breakeven,
    int DaysToExpiration,
    string Moneyness,
    decimal CostPerContract);
