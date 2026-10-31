using System.Globalization;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Account;

/// <summary>Which Alpaca account(s) a read covers.</summary>
public enum AccountScope
{
    All,
    Paper,
    Live,
}

public sealed record AccountScopeArgs
{
    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

/// <summary>Reads both Alpaca accounts (or one) for the owner through <see cref="AccountSummaryService"/>.</summary>
internal static class AccountReads
{
    public static async Task<List<AccountDetail>> LoadAsync(OperationContext ctx, AccountScope scope)
    {
        var owner = ctx.RequireOwner();
        var service = ctx.Get<AccountSummaryService>();
        var wanted = scope switch
        {
            AccountScope.Paper => new[] { true },
            AccountScope.Live => new[] { false },
            _ => new[] { true, false },
        };
        var details = await Task.WhenAll(wanted.Select(isPaper => service.GetDetailAsync(owner, isPaper, ctx.CancellationToken)));
        return [.. details];
    }

    public static string Name(AccountDetail d) => d.IsPaper ? "Paper" : "Live";

    public static string Money(decimal? v) => v is { } m ? Fmt.Money(m) : "-";

    public static string Signed(decimal? v) => v is { } m ? Fmt.SignedMoney(m) : "-";

    public static string Pct(decimal? part, decimal? whole) =>
        part is { } p && whole is { } w && w != 0 ? Fmt.Percent(p / w, 2, signed: true) : "-";
}

[Operation("account summary", Summary = "Equity, cash, buying power and today's true P&L (deposits excluded) for your Alpaca paper and live accounts.", Risk = OperationRisk.Read)]
[Example("idiotproof account summary")]
[Example("idiotproof account summary --mode live")]
public sealed class AccountSummaryOperation : IOperation<AccountScopeArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var details = await AccountReads.LoadAsync(ctx, args.Mode);
        var days = await Task.WhenAll(details.Select(d => d.Error is null ? TodayAsync(ctx, d.IsPaper) : Task.FromResult<PortfolioHistory?>(null)));
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Account").Column("Equity", Align.Right).Column("Today", Align.Right).Column("Today %", Align.Right)
            .Column("Cash", Align.Right).Column("Buying power", Align.Right).Column("Positions", Align.Right).Column("Status");
        var data = new List<object>();
        foreach (var (d, day) in details.Zip(days))
        {
            if (d.Error is not null)
            {
                table.Row(AccountReads.Name(d), "-", "-", "-", "-", "-", "-", d.Error);
                data.Add(new { account = AccountReads.Name(d).ToLowerInvariant(), error = d.Error });
                continue;
            }
            decimal? equity = d.Number("equity"), lastEquity = d.Number("last_equity");
            // True day P&L from portfolio history (deposits/withdrawals excluded); equity minus
            // last_equity only when that read fails, since it counts a deposit as a gain.
            var trueDay = day?.ProfitLoss is not null;
            decimal? today = trueDay ? day!.ProfitLoss : equity - lastEquity;
            decimal? todayBase = trueDay && day!.BaseValue is { } b && b != 0 ? b : lastEquity;
            var status = d.Account.TryGetValue("status", out var s) ? s : "";
            if (d.Account.TryGetValue("pattern_day_trader", out var pdt) && pdt == "True") status += ", PDT";
            if (d.Account.TryGetValue("trading_blocked", out var tb) && tb == "True") status += ", TRADING BLOCKED";
            table.Row(AccountReads.Name(d), AccountReads.Money(equity), AccountReads.Signed(today), AccountReads.Pct(today, todayBase),
                AccountReads.Money(d.Number("cash")), AccountReads.Money(d.Number("buying_power")),
                d.Positions.Count.ToString(CultureInfo.InvariantCulture), status);
            data.Add(new
            {
                account = AccountReads.Name(d).ToLowerInvariant(),
                accountNumber = d.Account.GetValueOrDefault("account_number"),
                status = d.Account.GetValueOrDefault("status"),
                equity, lastEquity, today,
                todaySource = trueDay ? "portfolio_history.profit_loss" : "equity-last_equity",
                cash = d.Number("cash"),
                buyingPower = d.Number("buying_power"),
                dayTradingBuyingPower = d.Number("daytrading_buying_power"),
                longMarketValue = d.Number("long_market_value"),
                shortMarketValue = d.Number("short_market_value"),
                daytradeCount = d.Number("daytrade_count"),
                patternDayTrader = d.Account.GetValueOrDefault("pattern_day_trader") == "True",
                positions = d.Positions.Count,
            });
        }
        return OperationResult.Ok(data, table.Render());
    }

    /// <summary>The account's 1-day portfolio history, or null when it can't be read (the caller falls back).</summary>
    private static async Task<PortfolioHistory?> TodayAsync(OperationContext ctx, bool isPaper)
    {
        if (ctx.Services.GetService(typeof(AlpacaApiFactory)) is not AlpacaApiFactory factory) return null;
        try
        {
            await using var api = await factory.ForOwnerAsync(ctx.RequireOwner(), isPaper ? BrokerMode.Paper : BrokerMode.Live, ctx.CancellationToken);
            return await PortfolioHistory.TodayAsync(api, ctx.CancellationToken);
        }
        catch (Exception ex) when (ex is AlpacaAccessException or AlpacaApiException or HttpRequestException or TaskCanceledException && !ctx.CancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

[Operation("positions list", Summary = "Every open position in your Alpaca paper and live accounts: price, today's move, unrealized P&L.", Risk = OperationRisk.Read)]
[Example("idiotproof positions list")]
[Example("idiotproof positions list --mode paper")]
public sealed class PositionsListOperation : IOperation<AccountScopeArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var details = await AccountReads.LoadAsync(ctx, args.Mode);
        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var d in details)
        {
            var header = $"{AccountReads.Name(d)} account";
            if (d.Error is not null)
            {
                blocks.Add($"{header}: {d.Error}");
                data.Add(new { account = AccountReads.Name(d).ToLowerInvariant(), error = d.Error });
                continue;
            }
            if (d.Positions.Count == 0)
            {
                blocks.Add($"{header}: no open positions.");
                data.Add(new { account = AccountReads.Name(d).ToLowerInvariant(), positions = Array.Empty<object>() });
                continue;
            }

            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Symbol").Column("Qty", Align.Right).Column("Avg cost", Align.Right).Column("Price", Align.Right)
                .Column("Today", Align.Right).Column("Value", Align.Right).Column("Unrealized", Align.Right).Column("P&L %", Align.Right);
            foreach (var p in d.Positions.OrderByDescending(p => Math.Abs(p.MarketValue)))
            {
                var cost = p.AveragePrice * p.Quantity * (p.Option?.Multiplier ?? 1);
                table.Row(p.Symbol, Fmt.Number(p.Quantity), AccountReads.Money(p.AveragePrice), AccountReads.Money(p.CurrentPrice),
                    AccountReads.Signed(p.UnrealizedIntradayPnl), AccountReads.Money(p.MarketValue),
                    AccountReads.Signed(p.UnrealizedPnl), AccountReads.Pct(p.UnrealizedPnl, Math.Abs(cost)));
            }
            var totalValue = d.Positions.Sum(p => p.MarketValue);
            var totalPnl = d.Positions.Sum(p => p.UnrealizedPnl);
            var totalToday = d.Positions.All(p => p.UnrealizedIntradayPnl is null) ? (decimal?)null : d.Positions.Sum(p => p.UnrealizedIntradayPnl ?? 0);
            table.Row("TOTAL", "", "", "", AccountReads.Signed(totalToday), AccountReads.Money(totalValue), AccountReads.Signed(totalPnl), "");
            blocks.Add($"{header} ({d.Positions.Count} positions)\n{table.Render()}");
            data.Add(new
            {
                account = AccountReads.Name(d).ToLowerInvariant(),
                positions = d.Positions.Select(p => new
                {
                    p.Symbol, p.Quantity, p.AveragePrice, p.CurrentPrice, p.LastDayPrice, p.MarketValue,
                    p.UnrealizedPnl, p.UnrealizedIntradayPnl, assetClass = p.AssetClass.ToString().ToLowerInvariant(),
                }),
                totalValue, totalPnl, totalToday,
            });
        }
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}
