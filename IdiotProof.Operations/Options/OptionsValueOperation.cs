using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Shared.Options;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

public sealed record OptionsValueArgs
{
    [Argument(0, Name = "occ", Description = "OCC option symbol: ROOT + YYMMDD + C|P + strike x 1000 (8 digits), e.g. AAPL261120C00250000.")]
    public string Occ { get; init; } = "";

    [Option("spot", Description = "Underlying price to value at (what-if) instead of Alpaca's last trade.")]
    public decimal? Spot { get; init; }

    [Option("premium", Description = "Premium per share to value instead of the current mid, e.g. what you paid.")]
    public decimal? Premium { get; init; }

    [Option("iv", Description = "Implied volatility in percent for the Black-Scholes price, e.g. 35 (default Alpaca's IV).")]
    public decimal? Iv { get; init; }

    [Option("rate", Description = "Risk-free rate in percent, e.g. 4.25 (default: the Options page setting, else 4).")]
    public decimal? Rate { get; init; }

    [Option("feed", Description = "Options data feed: indicative (default, free) or opra (paid).")]
    public OptionsFeed Feed { get; init; } = OptionsFeed.Indicative;
}

/// <summary>
/// What a contract is worth and why: intrinsic vs extrinsic (time) value of the premium, breakeven at
/// expiry, days to expiry, cost per contract, and the Black-Scholes theoretical price next to the market.
/// </summary>
[Operation("options value", Summary = "Intrinsic and extrinsic value, breakeven, DTE and Black-Scholes price for one option contract.", Risk = OperationRisk.Read)]
[Example("idiotproof options value AAPL261120C00250000")]
[Example("idiotproof options value AAPL261120C00250000 --spot 260 --premium 4.10")]
public sealed class OptionsValueOperation : IOperation<OptionsValueArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsValueArgs args, OperationContext ctx)
    {
        if (args.Premium is <= 0) return OperationResult.Usage("--premium must be greater than 0.");
        if (args.Iv is <= 0 or > 1000) return OperationResult.Usage("--iv is a percent between 0 and 1000, e.g. 35.");
        var m = await LoadModelInputsAsync(ctx, args.Occ, args.Feed, args.Spot, args.Rate);
        var c = m.Contract;
        if (m.Spot is not { } spot)
            return OperationResult.Fail($"The {c.UnderlyingSymbol} price is unknown; pass --spot.");

        var premium = args.Premium ?? m.Quote.Mid;
        var premiumSource = args.Premium is null ? "mid" : "--premium";
        var intrinsic = Intrinsic(c, spot);
        decimal? extrinsic = premium > 0 ? premium - intrinsic : null;
        decimal? breakeven = premium > 0 ? c.Right == OptionRight.Call ? c.Strike + premium : c.Strike - premium : null;
        decimal? breakevenMove = breakeven is { } be ? (be - spot) / spot : null;

        decimal? iv = args.Iv is { } p ? p / 100m : m.Quote.ImpliedVolatility;
        var ivSource = args.Iv is not null ? "--iv" : iv is not null ? "Alpaca" : null;
        if (iv is null && premium > 0 && SolveIv(c, spot, premium, m.Years, m.Rate) is { } solved)
        {
            iv = (decimal)solved;
            ivSource = $"solved from the {premiumSource}";
        }
        decimal? theoretical = iv is { } sigma
            ? (decimal)BlackScholesCalculator.TheoreticalPrice((double)spot, (double)c.Strike, m.Years, m.Rate, (double)sigma, c.Right)
            : null;

        var x = c.Multiplier;
        var block = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Contract", $"{c.OccSymbol}  ({c.DisplayName})")
            .Add("Spot", $"{Money(spot)} ({m.SpotSource}), {Moneyness(c, spot)}")
            .Add("Premium", premium > 0 ? $"{Money(premium)} ({premiumSource}), {Fmt.Money(premium * x)} per contract" : "- (no quote; pass --premium)")
            .Add("Intrinsic", $"{Money(intrinsic)}  ({Fmt.Money(intrinsic * x)} per contract)")
            .Add("Extrinsic", extrinsic is { } ex ? $"{Money(ex)}  ({Fmt.Money(ex * x)} per contract, time value that decays to 0 by expiry)" : "-")
            .Add("Breakeven", breakeven is { } b ? $"{Money(b)} at expiry ({Fmt.Percent(breakevenMove!.Value, 2, signed: true)} from spot)" : "-")
            .Add("Expires", $"{Date(c.Expiration)} 4:00 PM ET, {m.Dte} DTE")
            .Add("Black-Scholes", theoretical is { } t
                ? $"{Money(t)} at IV {Iv(iv)} ({ivSource}), rate {Fmt.Percent(m.Rate, 2)}" +
                  (premium > 0 ? $"; market is {Fmt.SignedMoney(premium - t)} vs model" : "")
                : "- (no IV; pass --iv)");

        var data = new
        {
            occ = c.OccSymbol,
            underlying = c.UnderlyingSymbol,
            type = c.Right.ToString().ToLowerInvariant(),
            strike = c.Strike,
            expiration = Date(c.Expiration),
            dte = m.Dte,
            spot,
            moneyness = Moneyness(c, spot),
            premium = premium > 0 ? premium : (decimal?)null,
            premiumSource,
            intrinsic,
            extrinsic,
            breakeven,
            breakevenMove,
            costPerContract = premium > 0 ? premium * x : (decimal?)null,
            impliedVolatility = iv,
            ivSource,
            rate = m.Rate,
            theoretical,
            multiplier = x,
        };
        return OperationResult.Ok(data, block.Render());
    }
}
