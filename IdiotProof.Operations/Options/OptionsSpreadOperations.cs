using System.Globalization;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Trading;
using IdiotProof.Shared.Options;

namespace IdiotProof.Operations.Options;

/// <summary>Arguments for a multi-leg options order (Alpaca <c>order_class=mleg</c>).</summary>
public sealed record OptionsSpreadArgs
{
    [Argument(0, Name = "legs", Description = "2 to 4 legs, each side:OCC[:ratio][:close], e.g. buy:AAPL261219C00150000 sell:AAPL261219C00160000. Legs open unless :close.")]
    public string[] Legs { get; init; } = [];

    [Option("qty", Description = "How many spreads (each leg trades ratio x qty contracts). Default 1.", Short = 'q')]
    public int Quantity { get; init; } = 1;

    [Option("limit", Description = "Net price per spread per share: positive = debit you pay, negative = credit you take. Without it the order is a market order.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("mode", Description = "paper (default) or live (needs confirmation). The Sandbox simulator does not fill multi-leg orders.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

/// <summary>One parsed spread leg: the contract, which way, its ratio and whether it opens.</summary>
public sealed record SpreadLeg(OptionContract Contract, OrderSide Side, int Ratio, bool Opens)
{
    public string PositionIntent => (Side == OrderSide.Buy ? "buy" : "sell") + (Opens ? "_to_open" : "_to_close");
}

/// <summary>Payoff at expiry of a spread, per spread unit and in dollars for the whole order.</summary>
public sealed record SpreadRisk(decimal? MaxLoss, decimal? MaxGain, bool PremiumIncluded, string? Unavailable);

/// <summary>Parses legs and computes the spread's worst and best case at expiry.</summary>
public static class OptionsSpreadMath
{
    public static SpreadLeg ParseLeg(string text)
    {
        var parts = text.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 4)
            throw OperationException.Usage($"Leg '{text}' should be side:OCC[:ratio][:close], e.g. buy:AAPL261219C00150000.");
        var side = parts[0].ToLowerInvariant() switch
        {
            "buy" or "b" => OrderSide.Buy,
            "sell" or "s" => OrderSide.Sell,
            _ => throw OperationException.Usage($"Leg '{text}': the side is buy or sell."),
        };
        var contract = OptionsTradeFlow.ParseContract(parts[1]);
        var ratio = 1;
        var opens = true;
        foreach (var extra in parts.Skip(2))
        {
            if (extra.Equals("close", StringComparison.OrdinalIgnoreCase)) opens = false;
            else if (extra.Equals("open", StringComparison.OrdinalIgnoreCase)) opens = true;
            else if (!int.TryParse(extra, NumberStyles.None, CultureInfo.InvariantCulture, out ratio) || ratio <= 0)
                throw OperationException.Usage($"Leg '{text}': '{extra}' is neither a positive ratio nor open/close.");
        }
        return new SpreadLeg(contract, side, ratio, opens);
    }

    /// <summary>Why these legs can't be one Alpaca multi-leg order, or null.</summary>
    public static string? Problem(IReadOnlyList<SpreadLeg> legs)
    {
        if (legs.Count is < 2 or > 4) return "A spread has 2 to 4 legs.";
        if (legs.Select(l => l.Contract.OccSymbol).Distinct().Count() != legs.Count) return "Each leg must be a different contract.";
        if (legs.Select(l => l.Contract.UnderlyingSymbol).Distinct().Count() != 1) return "Every leg must be on the same underlying.";
        if (legs.Select(l => l.Ratio).Aggregate(Gcd) != 1) return "Write the ratios in simplest form (e.g. 1:2, not 2:4) and scale with --qty.";
        return null;
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>
    /// Worst and best P&amp;L at expiry for opening spreads whose legs share one expiration: the
    /// payoff is piecewise linear in the underlying, so it is checked at zero, at every strike and
    /// by its slope above the highest strike (a negative slope there = unlimited loss). The net
    /// premium (the limit) is included when known; dollars are for the whole order (× qty × 100).
    /// </summary>
    public static SpreadRisk Analyze(IReadOnlyList<SpreadLeg> legs, decimal? netLimit, int quantity)
    {
        if (legs.Any(l => !l.Opens)) return new(null, null, false, "the order closes legs, so its risk depends on what you already hold");
        if (legs.Select(l => l.Contract.Expiration).Distinct().Count() != 1) return new(null, null, false, "the legs expire on different dates");

        decimal Payoff(decimal s) => legs.Sum(l =>
            (l.Side == OrderSide.Buy ? 1 : -1) * l.Ratio
            * (l.Contract.Right == OptionRight.Call ? Math.Max(s - l.Contract.Strike, 0m) : Math.Max(l.Contract.Strike - s, 0m)));
        var tailSlope = legs.Where(l => l.Contract.Right == OptionRight.Call).Sum(l => (l.Side == OrderSide.Buy ? 1 : -1) * l.Ratio);

        var points = legs.Select(l => l.Contract.Strike).Append(0m).Distinct().ToList();
        var premium = netLimit ?? 0m;
        var values = points.Select(s => Payoff(s) - premium).ToList();
        var scale = quantity * legs[0].Contract.Multiplier;
        decimal? maxLoss = tailSlope < 0 ? null : Math.Max(0m, -values.Min()) * scale;
        decimal? maxGain = tailSlope > 0 ? null : Math.Max(0m, values.Max()) * scale;
        return new(maxLoss, maxGain, netLimit is not null, null);
    }

    public static string Describe(SpreadRisk risk)
    {
        if (risk.Unavailable is not null) return $"  max loss   not computed: {risk.Unavailable}";
        var suffix = risk.PremiumIncluded ? "" : " (before the premium: market order, net price unknown)";
        return $"  max loss   {(risk.MaxLoss is { } l ? Fmt.Money(l) : "UNLIMITED")} at expiry{suffix}\n"
             + $"  max gain   {(risk.MaxGain is { } g ? Fmt.Money(g) : "unlimited")} at expiry";
    }
}

[Operation("options spread", Summary = "Place a 2-4 leg options order (verticals, straddles, ratios...) as one Alpaca multi-leg order; level 3, max-loss preview, live needs a code.", Risk = OperationRisk.Money)]
[Example("idiotproof options spread buy:AAPL261219C00150000 sell:AAPL261219C00160000 --limit 3.10", Description = "Paper bull call spread for a $3.10 debit.")]
[Example("idiotproof options spread sell:SPY261120P00560000 buy:SPY261120P00550000 --limit -1.25 --qty 2 --mode live", Description = "Live put credit spread; prints a confirmation code first.")]
public sealed class OptionsSpreadOperation : IOperation<OptionsSpreadArgs>
{
    public async Task<OperationResult> ExecuteAsync(OptionsSpreadArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args.Mode == BrokerMode.Sandbox)
            return OperationResult.Usage("The Sandbox simulator does not fill multi-leg orders; use --mode paper (default) or --mode live.");
        if (args.Quantity <= 0) return OperationResult.Usage("--qty must be a positive number of spreads.");
        if (args.Limit == 0m) return OperationResult.Usage("--limit is a non-zero net price (positive = debit, negative = credit); leave it out for a market order.");

        var legs = args.Legs.Select(OptionsSpreadMath.ParseLeg).ToList();
        if (OptionsSpreadMath.Problem(legs) is { } problem) return OperationResult.Usage(problem);

        var client = await OptionsTradeFlow.ResolveAsync(ctx, args.Mode);
        if (client is not null)
        {
            int level;
            try { level = client.SupportsOptions ? (await client.GetOptionsAccountAsync(ctx.CancellationToken)).TradingLevel : 0; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return OperationResult.Blocked("Couldn't read the account's options approval level: " + ex.Message);
            }
            if (level < OptionsTradingLevel.Spreads)
                return OperationResult.Blocked($"Spreads need options level 3; this {args.Mode} account is level {level}. {OptionsTradingLevel.Describe(level)}");
        }

        var risk = OptionsSpreadMath.Analyze(legs, args.Limit, args.Quantity);
        var intent = new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = legs.Any(l => l.Opens) ? OrderPurpose.Entry : OrderPurpose.Exit,
            Origin = OrderOrigin.Options,
            Order = new OrderRequest
            {
                OrderClass = OrderClass.Mleg,
                AssetClass = AssetClass.Option,
                Quantity = args.Quantity,
                Side = args.Limit is < 0 ? OrderSide.Sell : OrderSide.Buy,
                Type = args.Limit is null ? OrderType.Market : OrderType.Limit,
                LimitPrice = args.Limit,
                TimeInForce = "DAY",
                Legs = legs.Select(l => new OrderLeg
                {
                    Symbol = l.Contract.OccSymbol, Side = l.Side, RatioQuantity = l.Ratio, PositionIntent = l.PositionIntent,
                }).ToList(),
            },
            ReferencePrice = args.Limit,
            Note = args.Note ?? $"{ctx.Caller.ToString().ToLowerInvariant()} options spread {legs[0].Contract.UnderlyingSymbol}",
        };

        var result = await TradeFlow.SubmitOrConfirmAsync(intent, ctx);
        var legTable = new TextTable { MaxWidth = ctx.Width }.Column("side").Column("ratio", Align.Right).Column("contract").Column("intent");
        foreach (var l in legs) legTable.Row(l.Side.ToString().ToLowerInvariant(), l.Ratio.ToString(CultureInfo.InvariantCulture), $"{l.Contract.DisplayName} ({l.Contract.OccSymbol})", l.PositionIntent);
        var header = legTable.Render() + "\n" + OptionsSpreadMath.Describe(risk);
        return result with
        {
            Human = header + "\n\n" + result.Human,
            Data = new
            {
                Legs = legs.Select(l => new { Symbol = l.Contract.OccSymbol, Side = l.Side.ToString().ToLowerInvariant(), l.Ratio, l.PositionIntent }),
                risk.MaxLoss,
                risk.MaxGain,
                MaxLossUnlimited = risk.Unavailable is null && risk.MaxLoss is null,
                risk.PremiumIncluded,
                RiskNote = risk.Unavailable,
                Gateway = result.Data,
            },
        };
    }
}
