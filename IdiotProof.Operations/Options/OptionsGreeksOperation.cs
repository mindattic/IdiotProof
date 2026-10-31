using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>Arguments for the reads that run the local Black-Scholes model on one contract.</summary>
public sealed record OptionsModelArgs
{
    [Argument(0, Name = "occ", Description = "OCC option symbol: ROOT + YYMMDD + C|P + strike x 1000 (8 digits), e.g. AAPL261120C00250000.")]
    public string Occ { get; init; } = "";

    [Option("spot", Description = "Underlying price to use instead of Alpaca's last trade.")]
    public decimal? Spot { get; init; }

    [Option("rate", Description = "Risk-free rate in percent, e.g. 4.25 (default: the Options page setting, else 4).")]
    public decimal? Rate { get; init; }

    [Option("feed", Description = "Options data feed: indicative (default, free) or opra (paid).")]
    public OptionsFeed Feed { get; init; } = OptionsFeed.Indicative;
}

/// <summary>
/// A contract's Greeks. Alpaca's own when its snapshot carries them; otherwise (0DTE, missing inputs)
/// the local Black-Scholes model, badged <c>Model</c>, on Alpaca's IV or, when that is missing too,
/// on an IV solved locally from the mid.
/// </summary>
[Operation("options greeks", Summary = "Delta, gamma, theta, vega and rho for one option contract; badged Model when computed locally.", Risk = OperationRisk.Read)]
[Example("idiotproof options greeks AAPL261120C00250000")]
[Example("idiotproof options greeks SPY261218P00600000 --spot 640 --rate 4.25")]
public sealed class OptionsGreeksOperation : IOperation<OptionsModelArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsModelArgs args, OperationContext ctx)
    {
        var m = await LoadModelInputsAsync(ctx, args.Occ, args.Feed, args.Spot, args.Rate);
        var c = m.Contract;
        var q = m.Quote;

        double delta, gamma, theta, vega, rho;
        decimal? iv = q.ImpliedVolatility;
        string source, ivSource;
        if (q.Greeks is { } g)
        {
            (delta, gamma, theta, vega, rho) = ((double)g.Delta, (double)g.Gamma, (double)g.Theta, (double)g.Vega, (double)g.Rho);
            source = "Alpaca";
            ivSource = iv is null ? "none" : "Alpaca";
        }
        else
        {
            if (m.Spot is not { } spot)
                return OperationResult.Fail($"Alpaca sent no Greeks for {c.OccSymbol} and the {c.UnderlyingSymbol} price is unknown; pass --spot.");
            ivSource = "Alpaca";
            if (iv is null)
            {
                var solved = SolveIv(c, spot, q.Mid, m.Years, m.Rate);
                if (solved is null)
                    return OperationResult.Fail($"Alpaca sent no Greeks or IV for {c.OccSymbol}, and no volatility reproduces the mid {Money(q.Mid)} at spot {Money(spot)}.");
                iv = (decimal)solved.Value;
                ivSource = $"Model (solved from mid {Money(q.Mid)})";
            }
            var mg = Greeks((double)spot, (double)c.Strike, m.Years, m.Rate, (double)iv.Value, c.Right);
            (delta, gamma, theta, vega, rho) = (mg.Delta, mg.Gamma, mg.Theta, mg.Vega, mg.Rho);
            source = "Model";
        }

        var x = c.Multiplier;
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Greek").Column("Per share", Align.Right).Column("Per contract", Align.Right).Column("Means");
        table.Row("Delta", Greek(delta), Fmt.SignedMoney((decimal)(delta * x)), $"P&L per $1 move in {c.UnderlyingSymbol}");
        table.Row("Gamma", Greek(gamma), Greek(gamma * x, 2), "change in delta per $1 move (per contract: in shares)");
        table.Row("Theta", Greek(theta), Fmt.SignedMoney((decimal)(theta * x)), "time decay per calendar day");
        table.Row("Vega", Greek(vega), Fmt.SignedMoney((decimal)(vega * x)), "P&L per 1 point of implied volatility");
        table.Row("Rho", Greek(rho), Fmt.SignedMoney((decimal)(rho * x)), "P&L per 1 point of interest rates");

        var badge = source == "Model" ? "[Model]" : "[Alpaca]";
        var head = $"{c.OccSymbol}  ({c.DisplayName}, {m.Dte} DTE)  {badge}\n" +
                   $"IV {Iv(iv)} ({ivSource})   spot {Money(m.Spot)} ({m.SpotSource})   mid {Money(q.Mid)}" +
                   (source == "Model" ? $"   rate {Fmt.Percent(m.Rate, 2)}   Black-Scholes, European, no dividends" : "");

        var data = new
        {
            occ = c.OccSymbol,
            source = source.ToLowerInvariant(),
            ivSource,
            impliedVolatility = iv,
            spot = m.Spot,
            mid = q.Mid,
            rate = source == "Model" ? m.Rate : (double?)null,
            dte = m.Dte,
            delta, gamma, theta, vega, rho,
            multiplier = x,
        };
        return OperationResult.Ok(data, head + "\n" + table.Render());
    }
}
