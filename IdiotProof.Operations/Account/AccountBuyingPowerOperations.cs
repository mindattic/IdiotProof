using System.Globalization;
using System.Text.Json;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Account;

/// <summary>Reads <c>GET /v2/account</c> for each account in scope.</summary>
internal static class AccountObjects
{
    public static Task<List<AccountRead<JsonElement>>> LoadAsync(OperationContext ctx, AccountScope scope) =>
        AccountApis.ForEachAsync(ctx, scope, api => api.GetAsync("/v2/account", ct: ctx.CancellationToken));
}

/// <summary>
/// Every buying-power figure Alpaca reports (overall, Reg T overnight, day-trading, non-marginable,
/// options) next to cash and the margin multiplier, for the paper and/or live account.
/// </summary>
[Operation("account buying-power", Summary = "Buying power by kind (overall, Reg T, day-trading, non-marginable, options), cash and margin multiplier.", Risk = OperationRisk.Read)]
[Example("idiotproof account buying-power")]
[Example("idiotproof account buying-power --mode live")]
public sealed class AccountBuyingPowerOperation : IOperation<AccountScopeArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await AccountObjects.LoadAsync(ctx, args.Mode);
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Account").Column("Buying power", Align.Right).Column("Reg T", Align.Right).Column("Day-trade", Align.Right)
            .Column("Non-marginable", Align.Right).Column("Options", Align.Right).Column("Cash", Align.Right).Column("Multiplier", Align.Right);
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                table.Row(r.Name, "-", "-", "-", "-", "-", "-", r.Error);
                data.Add(new { account = r.Key, error = r.Error });
                continue;
            }
            var a = r.Value;
            decimal? bp = AccountApis.Dec(a, "buying_power"), regt = AccountApis.Dec(a, "regt_buying_power"),
                dt = AccountApis.Dec(a, "daytrading_buying_power"), nonMarginable = AccountApis.Dec(a, "non_marginable_buying_power"),
                options = AccountApis.Dec(a, "options_buying_power"), cash = AccountApis.Dec(a, "cash"), multiplier = AccountApis.Dec(a, "multiplier");
            table.Row(r.Name, AccountApis.Money(bp), AccountApis.Money(regt), AccountApis.Money(dt), AccountApis.Money(nonMarginable),
                AccountApis.Money(options), AccountApis.Money(cash), multiplier is { } m ? Fmt.Number(m) + "x" : "-");
            data.Add(new
            {
                account = r.Key, buyingPower = bp, regTBuyingPower = regt, dayTradingBuyingPower = dt,
                nonMarginableBuyingPower = nonMarginable, optionsBuyingPower = options, cash, multiplier,
            });
        }
        return OperationResult.Ok(data, table.Render());
    }
}

/// <summary>
/// Pattern-day-trader standing: the rolling 5-business-day day-trade count, the PDT flag, equity
/// against the $25,000 floor and how many day trades are left before the flag trips.
/// </summary>
[Operation("account pdt", Summary = "Pattern-day-trader standing: day trades in the last 5 business days, PDT flag, equity vs the $25k floor, trades left.", Risk = OperationRisk.Read)]
[Example("idiotproof account pdt")]
[Example("idiotproof account pdt --mode live")]
public sealed class AccountPdtOperation : IOperation<AccountScopeArgs>
{
    /// <summary>FINRA's minimum equity for unlimited day trading in a margin account.</summary>
    public const decimal PdtEquityFloor = 25_000m;

    /// <summary>The 4th day trade in 5 business days flags an account under the floor.</summary>
    public const int DayTradesAllowed = 3;

    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await AccountObjects.LoadAsync(ctx, args.Mode);
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Account").Column("Day trades (5d)", Align.Right).Column("PDT flag").Column("Equity", Align.Right)
            .Column("Day trades left", Align.Right).Column("Standing");
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                table.Row(r.Name, "-", "-", "-", "-", r.Error);
                data.Add(new { account = r.Key, error = r.Error });
                continue;
            }
            var a = r.Value;
            var count = (int)(AccountApis.Dec(a, "daytrade_count") ?? 0);
            var flagged = AccountApis.Bool(a, "pattern_day_trader");
            var equity = AccountApis.Dec(a, "equity");
            var (left, standing) = Standing(count, flagged, equity);
            if (AccountApis.Bool(a, "trading_blocked")) standing = "TRADING BLOCKED; " + standing;
            table.Row(r.Name, count.ToString(CultureInfo.InvariantCulture), flagged ? "YES" : "no", AccountApis.Money(equity),
                left is { } n ? n.ToString(CultureInfo.InvariantCulture) : "unlimited", standing);
            data.Add(new { account = r.Key, dayTradeCount = count, patternDayTrader = flagged, equity, dayTradesLeft = left, standing });
        }
        return OperationResult.Ok(data, table.Render());
    }

    /// <summary>Day trades left (null = unlimited) and a one-line explanation.</summary>
    public static (int? Left, string Standing) Standing(int count, bool flagged, decimal? equity)
    {
        if (equity >= PdtEquityFloor)
            return (null, flagged ? "PDT, equity above $25k: day trading allowed" : "equity above $25k: no PDT limit");
        if (flagged)
            return (0, "PDT with equity under $25k: day trades are refused until equity is back above $25k");
        var left = Math.Max(0, DayTradesAllowed - count);
        return (left, left == 0
            ? "one more day trade flags the account as PDT"
            : $"{left} more day trade{(left == 1 ? "" : "s")} before the 4th in 5 business days flags PDT");
    }
}
