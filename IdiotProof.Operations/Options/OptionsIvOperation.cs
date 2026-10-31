using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>One premium the IV was solved at.</summary>
public sealed record IvPoint(string Price, decimal? Premium, double? ImpliedVolatility);

/// <summary>
/// Implied volatility for one contract: Alpaca's figure next to the local Black-Scholes solver's IV
/// at the bid, mid, ask and last trade, so the spread's effect on IV is visible.
/// </summary>
[Operation("options iv", Summary = "Implied volatility for one option contract: Alpaca's, plus IV solved locally at bid, mid, ask and last.", Risk = OperationRisk.Read)]
[Example("idiotproof options iv AAPL261120C00250000")]
[Example("idiotproof options iv AAPL261120C00250000 --spot 255")]
public sealed class OptionsIvOperation : IOperation<OptionsModelArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsModelArgs args, OperationContext ctx)
    {
        var m = await LoadModelInputsAsync(ctx, args.Occ, args.Feed, args.Spot, args.Rate);
        var c = m.Contract;
        var q = m.Quote;

        var points = new List<IvPoint>();
        if (m.Spot is { } spot)
        {
            void Add(string label, decimal? premium) =>
                points.Add(new IvPoint(label, premium is > 0 ? premium : null, premium is > 0 ? SolveIv(c, spot, premium.Value, m.Years, m.Rate) : null));
            Add("Bid", q.Bid);
            Add("Mid", q.Bid > 0 && q.Ask > 0 ? (q.Bid + q.Ask) / 2m : null);
            Add("Ask", q.Ask);
            Add("Last", q.LastTrade);
        }

        var table = new TextTable { MaxWidth = ctx.Width }.Column("Price").Column("Premium", Align.Right).Column("IV", Align.Right);
        table.Row("Alpaca", "", Iv(q.ImpliedVolatility));
        foreach (var p in points) table.Row(p.Price, Money(p.Premium), p.Premium is null ? "-" : p.ImpliedVolatility is { } v ? Iv(v) : "no solution");

        var head = $"{c.OccSymbol}  ({c.DisplayName}, {m.Dte} DTE)\n" +
                   $"spot {Money(m.Spot)} ({m.SpotSource})   rate {Fmt.Percent(m.Rate, 2)}   Black-Scholes, European, no dividends";
        var note = m.Spot is null ? $"\nThe {c.UnderlyingSymbol} price is unknown, so only Alpaca's IV is shown; pass --spot to solve locally." : "";

        var data = new
        {
            occ = c.OccSymbol,
            alpaca = q.ImpliedVolatility,
            spot = m.Spot,
            rate = m.Rate,
            dte = m.Dte,
            solved = points,
        };
        return OperationResult.Ok(data, head + "\n" + table.Render() + note);
    }
}
