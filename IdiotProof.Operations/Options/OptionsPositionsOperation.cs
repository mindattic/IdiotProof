using System.Text.Json;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>
/// Open option positions in the owner's Alpaca accounts (<c>GET /v2/positions</c>, asset class
/// <c>us_option</c>), each decoded from its OCC symbol into underlying, right, strike, expiry and DTE.
/// </summary>
[Operation("options positions", Summary = "Open option contracts in your Alpaca accounts: strike, expiry, DTE, cost, value and unrealized P&L.", Risk = OperationRisk.Read)]
[Example("idiotproof options positions")]
[Example("idiotproof options positions --mode live")]
public sealed class OptionsPositionsOperation : IOperation<AccountScopeArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await PerAccountAsync(ctx, args.Mode, (api, ct) => api.GetAsync("/v2/positions", null, ct));
        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var r in reads)
        {
            var header = r.Account == "paper" ? "Paper account" : "Live account";
            if (r.Error is not null)
            {
                blocks.Add($"{header}: {r.Error}");
                data.Add(new { account = r.Account, error = r.Error });
                continue;
            }

            var rows = (r.Value.ValueKind == JsonValueKind.Array ? r.Value.EnumerateArray().ToList() : [])
                .Where(p => string.Equals(Str(p, "asset_class"), "us_option", StringComparison.OrdinalIgnoreCase))
                .Select(p => Row(p, ctx.UtcNow))
                .OrderBy(p => p.Expiration).ThenBy(p => p.Underlying).ThenBy(p => p.Strike)
                .ToList();
            if (rows.Count == 0)
            {
                blocks.Add($"{header}: no open option positions.");
                data.Add(new { account = r.Account, positions = Array.Empty<object>() });
                continue;
            }

            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Contract").Column("Type").Column("Strike", Align.Right).Column("Expiry").Column("DTE", Align.Right)
                .Column("Qty", Align.Right).Column("Avg", Align.Right).Column("Price", Align.Right)
                .Column("Value", Align.Right).Column("Unrealized", Align.Right).Column("P&L %", Align.Right);
            foreach (var p in rows)
            {
                table.Row(p.Symbol, p.Right, p.Strike is { } k ? Strike(k) : "-", p.Expiration is { } e ? Date(e) : "-",
                    p.Dte?.ToString() ?? "-", Fmt.Number(p.Quantity), Money(p.AverageEntryPrice), Money(p.CurrentPrice),
                    Money(p.MarketValue), p.UnrealizedPnl is { } u ? Fmt.SignedMoney(u) : "-",
                    p.UnrealizedPnlPercent is { } pct ? Fmt.Percent(pct, 2, signed: true) : "-");
            }
            var totalValue = rows.Sum(p => p.MarketValue ?? 0m);
            var totalPnl = rows.Sum(p => p.UnrealizedPnl ?? 0m);
            table.Row("TOTAL", "", "", "", "", "", "", "", Fmt.Money(totalValue), Fmt.SignedMoney(totalPnl), "");
            blocks.Add($"{header} ({rows.Count} option positions)\n{table.Render()}");
            data.Add(new { account = r.Account, positions = rows, totalValue, totalPnl });
        }
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }

    /// <summary>One option position as the JSON payload carries it.</summary>
    public sealed record OptionPositionRow(
        string Symbol, string Underlying, string Right, decimal? Strike, DateOnly? Expiration, int? Dte, string Side,
        decimal Quantity, decimal? AverageEntryPrice, decimal? CurrentPrice, decimal? CostBasis, decimal? MarketValue,
        decimal? UnrealizedPnl, decimal? UnrealizedPnlPercent, decimal? UnrealizedIntradayPnl);

    private static OptionPositionRow Row(JsonElement p, DateTime nowUtc)
    {
        var symbol = Str(p, "symbol") ?? "";
        var contract = OptionContract.ParseOcc(symbol);
        return new OptionPositionRow(
            symbol,
            contract?.UnderlyingSymbol ?? "",
            contract?.Right.ToString() ?? "-",
            contract?.Strike,
            contract?.Expiration,
            contract is null ? null : DaysToExpiry(contract, nowUtc),
            Str(p, "side") ?? "",
            Dec(p, "qty") ?? 0m,
            Dec(p, "avg_entry_price"),
            Dec(p, "current_price"),
            Dec(p, "cost_basis"),
            Dec(p, "market_value"),
            Dec(p, "unrealized_pl"),
            Dec(p, "unrealized_plpc"),
            Dec(p, "unrealized_intraday_pl"));
    }
}
