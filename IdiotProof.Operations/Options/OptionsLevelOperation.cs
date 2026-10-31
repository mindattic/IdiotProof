using System.Text.Json;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using static IdiotProof.Operations.Options.OptionsDataReads;

namespace IdiotProof.Operations.Options;

/// <summary>
/// The options-trading level of the owner's Alpaca paper and live accounts, from <c>GET /v2/account</c>:
/// <c>options_trading_level</c> (effective; it gates orders), <c>options_approved_level</c> (granted)
/// and <c>options_buying_power</c>. Note the plural <c>options_</c> prefix.
/// </summary>
[Operation("options level", Summary = "Options trading level (effective and approved) and options buying power for your Alpaca accounts.", Risk = OperationRisk.Read)]
[Example("idiotproof options level")]
[Example("idiotproof options level --mode paper")]
public sealed class OptionsLevelOperation : IOperation<AccountScopeArgs>
{
    /// <summary>What each Alpaca level allows (levels are cumulative).</summary>
    public static string Meaning(int level) => level switch
    {
        <= 0 => "options disabled",
        1 => "covered calls, cash-secured puts",
        2 => "level 1 + buy calls and puts",
        _ => "level 2 + spreads",
    };

    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await PerAccountAsync(ctx, args.Mode, (api, ct) => api.GetAsync("/v2/account", null, ct));
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Account").Column("Level", Align.Right).Column("Approved", Align.Right).Column("Allows")
            .Column("Options BP", Align.Right).Column("Status");
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                table.Row(r.Account, "-", "-", "-", "-", r.Error);
                data.Add(new { account = r.Account, error = r.Error });
                continue;
            }
            var a = r.Value;
            var level = (int)(Dec(a, "options_trading_level") ?? Dec(a, "option_trading_level") ?? 0m);
            var approved = (int)(Dec(a, "options_approved_level") ?? Dec(a, "option_approved_level") ?? level);
            var buyingPower = Dec(a, "options_buying_power");
            var status = Str(a, "status") ?? "";
            if (Bool(a, "trading_blocked") == true) status += ", TRADING BLOCKED";
            table.Row(r.Account, level.ToString(), approved.ToString(), Meaning(level), Money(buyingPower), status);
            data.Add(new
            {
                account = r.Account,
                accountNumber = Str(a, "account_number"),
                tradingLevel = level,
                approvedLevel = approved,
                allows = Meaning(level),
                optionsBuyingPower = buyingPower,
                status = Str(a, "status"),
            });
        }
        return OperationResult.Ok(data, table.Render());
    }
}
