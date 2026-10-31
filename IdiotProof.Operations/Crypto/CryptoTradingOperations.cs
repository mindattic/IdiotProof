using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Trading;

namespace IdiotProof.Operations.Crypto;

/// <summary>Arguments for a crypto buy or sell.</summary>
public sealed record CryptoOrderArgs
{
    [Argument(0, Name = "pair", Description = "Crypto pair, e.g. BTC/USD.")]
    public string Pair { get; init; } = "";

    [Argument(1, Name = "qty", Description = "Coin quantity, fractional allowed (0.0015). Leave out and pass --notional to size by dollars.", Required = false)]
    public decimal? Quantity { get; init; }

    [Option("notional", Description = "Size by dollar amount instead of coin quantity.")]
    public decimal? Notional { get; init; }

    [Option("limit", Description = "Limit price. Without it the order is a market order.", Short = 'l')]
    public decimal? Limit { get; init; }

    [Option("trigger", Description = "Stop trigger price: with --limit it makes a stop-limit order.")]
    public decimal? Trigger { get; init; }

    [Option("stop", Description = "Protective stop price for a buy; the Risk Guardian refuses entries without one.", Short = 's')]
    public decimal? Stop { get; init; }

    [Option("target", Description = "Take-profit price (for the reward:risk check).", Short = 't')]
    public decimal? Target { get; init; }

    [Option("tif", Description = "Time in force: gtc (default) or ioc.")]
    public string TimeInForce { get; init; } = "gtc";

    [Option("mode", Description = "paper (default) or live (needs a confirmation code). Sandbox does not simulate crypto.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why: kept in the audit trail.")]
    public string? Note { get; init; }
}

/// <summary>Builds crypto intents for the TransactionGateway; the gateway's crypto rules and the Live code do the rest.</summary>
internal static class CryptoOrders
{
    public static async Task<OrderIntent> BuildIntentAsync(OrderSide side, CryptoOrderArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var pair = CryptoData.RequirePair(args.Pair, "idiotproof crypto buy BTC/USD 0.001 --stop 65000");
        if (args.Mode == BrokerMode.Sandbox)
            throw OperationException.Usage("The Sandbox simulator trades stocks only; crypto orders go to --mode paper or --mode live.");
        if (args.Quantity is null && args.Notional is null) throw OperationException.Usage("Give a coin quantity or --notional.");
        if (args.Quantity is not null && args.Notional is not null) throw OperationException.Usage("Give a coin quantity or --notional, not both.");
        if (args.Quantity is <= 0 || args.Notional is <= 0) throw OperationException.Usage("The size must be positive.");
        if (args.Trigger is not null && args.Limit is null) throw OperationException.Usage("A --trigger needs a --limit (crypto stops are stop-limit orders).");

        decimal? reference = args.Limit;
        if (reference is null)
        {
            try
            {
                await using var api = await ctx.Get<AlpacaApiFactory>().ForDataAsync(owner, ctx.CancellationToken);
                var root = await api.GetDataAsync($"{CryptoData.Root}/latest/trades", new Dictionary<string, string?> { ["symbols"] = pair }, ctx.CancellationToken);
                reference = CryptoData.ForPair(root, "trades", pair) is { } t ? CryptoData.Num(t, "p") : null;
            }
            catch (Exception ex) when (ex is AlpacaAccessException or AlpacaApiException or HttpRequestException or JsonException)
            {
                // No quote: the Risk Guardian then asks for --limit on an entry.
            }
        }

        var buy = side == OrderSide.Buy;
        return new OrderIntent
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Purpose = buy ? OrderPurpose.Entry : OrderPurpose.Exit,
            Origin = OrderOrigin.Manual,
            Order = new OrderRequest
            {
                Symbol = pair,
                FractionalQuantity = args.Quantity,
                Notional = args.Notional,
                Side = side,
                Type = args.Trigger is not null ? OrderType.StopLimit : args.Limit is null ? OrderType.Market : OrderType.Limit,
                LimitPrice = args.Limit,
                StopPrice = args.Trigger,
                TimeInForce = args.TimeInForce.Trim().ToUpperInvariant(),
                AssetClass = AssetClass.Crypto,
            },
            ReferencePrice = reference,
            StopLossPrice = buy ? args.Stop : null,
            TakeProfitPrice = buy ? args.Target : null,
            Note = args.Note ?? $"{ctx.Caller.ToString().ToLowerInvariant()} crypto {(buy ? "buy" : "sell")}",
        };
    }
}

[Operation("crypto buy", Summary = "Buy a crypto pair (fractional, GTC/IOC) through the TransactionGateway. Paper by default; live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof crypto buy BTC/USD 0.001 --limit 70000 --stop 66500", Description = "Paper limit buy with a protective stop for the Risk Guardian.")]
[Example("idiotproof crypto buy ETH/USD --notional 250 --stop 2300 --mode live", Description = "Live dollar-sized buy; prints a confirmation code first.")]
public sealed class CryptoBuyOperation : IOperation<CryptoOrderArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoOrderArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await CryptoOrders.BuildIntentAsync(OrderSide.Buy, args, ctx), ctx);
}

[Operation("crypto sell", Summary = "Sell crypto you hold (crypto is long-only). Exits always clear the gates; live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof crypto sell BTC/USD 0.001")]
[Example("idiotproof crypto sell ETH/USD --notional 100 --limit 2600 --tif ioc --mode live")]
public sealed class CryptoSellOperation : IOperation<CryptoOrderArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoOrderArgs args, OperationContext ctx) =>
        await TradeFlow.SubmitOrConfirmAsync(await CryptoOrders.BuildIntentAsync(OrderSide.Sell, args, ctx), ctx);
}

[Operation("crypto positions", Summary = "Your crypto holdings in the Alpaca paper and live accounts: quantity, cost, price, unrealized P&L.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto positions")]
[Example("idiotproof crypto positions --mode live")]
public sealed class CryptoPositionsOperation : IOperation<AccountScopeArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await CryptoFunding.ReadAsync(ctx, args.Mode, "/v2/positions");
        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                blocks.Add($"{r.Name} account: {r.Error}");
                data.Add(new { account = r.Name.ToLowerInvariant(), error = r.Error });
                continue;
            }
            var positions = CryptoFunding.Items(r.Json)
                .Where(p => string.Equals(CryptoData.Str(p, "asset_class"), "crypto", StringComparison.OrdinalIgnoreCase))
                .Select(p =>
                {
                    var symbol = CryptoData.Str(p, "symbol") ?? "";
                    return new
                    {
                        pair = CryptoData.Normalize(symbol) ?? symbol,
                        qty = CryptoData.Num(p, "qty"),
                        avgCost = CryptoData.Num(p, "avg_entry_price"),
                        price = CryptoData.Num(p, "current_price"),
                        marketValue = CryptoData.Num(p, "market_value"),
                        costBasis = CryptoData.Num(p, "cost_basis"),
                        unrealized = CryptoData.Num(p, "unrealized_pl"),
                        unrealizedPct = CryptoData.Num(p, "unrealized_plpc"),
                        today = CryptoData.Num(p, "unrealized_intraday_pl"),
                    };
                })
                .OrderByDescending(p => Math.Abs(p.marketValue ?? 0))
                .ToList();
            data.Add(new { account = r.Name.ToLowerInvariant(), positions, totalValue = positions.Sum(p => p.marketValue ?? 0), totalUnrealized = positions.Sum(p => p.unrealized ?? 0) });
            if (positions.Count == 0) { blocks.Add($"{r.Name} account: no crypto positions."); continue; }

            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Pair").Column("Qty", Align.Right).Column("Avg cost", Align.Right).Column("Price", Align.Right)
                .Column("Today", Align.Right).Column("Value", Align.Right).Column("Unrealized", Align.Right).Column("P&L %", Align.Right);
            foreach (var p in positions)
                table.Row(p.pair, CryptoData.Size(p.qty), CryptoData.Price(p.avgCost), CryptoData.Price(p.price),
                    p.today is { } t ? Fmt.SignedMoney(t) : "-", p.marketValue is { } v ? Fmt.Money(v) : "-",
                    p.unrealized is { } u ? Fmt.SignedMoney(u) : "-", p.unrealizedPct is { } pc ? Fmt.Percent(pc, 2, signed: true) : "-");
            table.Row("TOTAL", "", "", "", "", Fmt.Money(positions.Sum(p => p.marketValue ?? 0)), Fmt.SignedMoney(positions.Sum(p => p.unrealized ?? 0)), "");
            blocks.Add($"{r.Name} account ({positions.Count} crypto positions)\n{table.Render()}");
        }
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}
