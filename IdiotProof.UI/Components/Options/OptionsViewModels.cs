using IdiotProof.Models;
using IdiotProof.Shared.Options;

namespace IdiotProof.UI.Components.Options;

/// <summary>One contract as the chain table shows it: catalog + live quote + every derived "no mental math" number.</summary>
public sealed record ChainRow(
    OptionContract Contract,
    OptionQuote? Quote,
    OptionValueBreakdown? Breakdown,
    decimal? ImpliedVolatility,
    /// <summary>"Alpaca" when the broker supplied IV, "Model" when solved locally, "—" when neither.</summary>
    string IvSource,
    decimal? ModelPrice)
{
    public decimal? Mid => Quote is { } q && q.Mid > 0m ? q.Mid : null;
}

/// <summary>What the order ticket hands back. The HOST decides on confirmation / elevation and places it.</summary>
public sealed record OptionOrderIntent(
    OptionContract Contract,
    OrderSide Side,
    int Contracts,
    OrderType Type,
    decimal? LimitPrice,
    string PositionIntent,
    decimal EstimatedPremiumPerShare)
{
    public decimal EstimatedTotal => EstimatedPremiumPerShare * Contracts * Contract.Multiplier;
    public bool IsOpening => PositionIntent.EndsWith("_to_open", StringComparison.Ordinal);
}

/// <summary>
/// An open option position with its live valuation and the informational sell-the-hype signal.
/// <paramref name="Observations"/> = how many extrinsic samples the host has banked for this
/// contract this session; the signal can't fire before <see cref="SellSignalEvaluator.MinObservations"/>.
/// </summary>
public sealed record OptionPositionView(
    Position Position,
    OptionQuote? Quote,
    decimal? UnderlyingPrice,
    OptionValueBreakdown? Breakdown,
    decimal? ImpliedVolatility,
    string IvSource,
    SellSignal? Signal,
    int Observations = 0)
{
    public bool IsWarmingUp => Position.Quantity > 0m && Signal is null && Observations < SellSignalEvaluator.MinObservations;
    public OptionContract Contract => Position.Option!;
    public decimal? Mid => Quote is { } q && q.Mid > 0m ? q.Mid : null;
    public decimal CostBasis => Position.AveragePrice * Math.Abs(Position.Quantity) * Contract.Multiplier;
    public decimal? MarketValueNow => Mid is { } m ? m * Math.Abs(Position.Quantity) * Contract.Multiplier : null;
    public decimal? UnrealizedPnl => MarketValueNow is { } mv ? (Position.Quantity >= 0 ? mv - CostBasis : CostBasis - mv) : null;
    public decimal? UnrealizedPnlPercent => UnrealizedPnl is { } p && CostBasis > 0m ? Math.Round(p / CostBasis * 100m, 1) : null;
}

/// <summary>
/// Turns raw contract + quote + underlying price into the numbers the components render.
/// Pure and shared by the chain view and the position tracker so both always agree.
/// IV precedence: the broker's server-side figure when present, else the local Black-Scholes
/// solve on the mid — badged so the user always knows which one they're looking at.
/// </summary>
public static class OptionsPresenter
{
    public static ChainRow BuildRow(OptionContract contract, OptionQuote? quote, decimal? underlyingPrice, double riskFreeRate, DateOnly today, DateTime nowUtc)
    {
        var (breakdown, iv, source, model) = Derive(contract, quote, underlyingPrice, riskFreeRate, today, nowUtc);
        return new ChainRow(contract, quote, breakdown, iv, source, model);
    }

    public static OptionPositionView BuildPosition(Position position, OptionQuote? quote, decimal? underlyingPrice, double riskFreeRate, DateOnly today, DateTime nowUtc, SellSignal? signal)
    {
        var (breakdown, iv, source, _) = Derive(position.Option!, quote, underlyingPrice, riskFreeRate, today, nowUtc);
        return new OptionPositionView(position, quote, underlyingPrice, breakdown, iv, source, signal);
    }

    private static (OptionValueBreakdown? Breakdown, decimal? Iv, string IvSource, decimal? ModelPrice) Derive(
        OptionContract contract, OptionQuote? quote, decimal? underlyingPrice, double riskFreeRate, DateOnly today, DateTime nowUtc)
    {
        var mid = quote is { } q && q.Mid > 0m ? q.Mid : (decimal?)null;
        var breakdown = underlyingPrice is { } spot && mid is { } premium
            ? IntrinsicValueCalculator.Breakdown(contract, spot, premium, today)
            : null;

        decimal? iv = quote?.ImpliedVolatility;
        var source = iv is { } ? "Alpaca" : "—";
        decimal? modelPrice = null;

        if (underlyingPrice is { } s && s > 0m)
        {
            var t = BlackScholesCalculator.YearsUntil(contract.Expiration, nowUtc);
            if (iv is null && mid is { } m)
            {
                var solved = BlackScholesCalculator.ImpliedVolatility((double)s, (double)contract.Strike, t, riskFreeRate, (double)m, contract.Right);
                if (solved is { } sv) { iv = (decimal)Math.Round(sv, 4); source = "Model"; }
            }
            if (iv is { } sigma && sigma > 0m)
                modelPrice = (decimal)Math.Round(BlackScholesCalculator.TheoreticalPrice((double)s, (double)contract.Strike, t, riskFreeRate, (double)sigma, contract.Right), 2);
        }

        return (breakdown, iv, source, modelPrice);
    }

    /// <summary>0–4 bucket for the "hype meter" colour scale on extrinsic-% of premium.</summary>
    public static int HypeBucket(decimal extrinsicPercent) => extrinsicPercent switch
    {
        >= 90m => 4,
        >= 65m => 3,
        >= 35m => 2,
        >= 10m => 1,
        _ => 0,
    };

    public static string Money(decimal? v, string dash = "—") => v is { } d ? d.ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture) : dash;
    public static string Pct(decimal? v, int decimals = 1, string dash = "—") => v is { } d ? d.ToString($"0.{new string('0', decimals)}", System.Globalization.CultureInfo.InvariantCulture) + "%" : dash;
    public static string Iv(decimal? v) => v is { } d ? (d * 100m).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "—";
}
