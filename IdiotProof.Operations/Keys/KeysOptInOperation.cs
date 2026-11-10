
using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Keys;

public sealed record KeysOptInArgs
{
    [Argument(0, Name = "broker", Description = "alpaca (route your orders to your own Alpaca account) or off (back to the Sandbox).")]
    public string Broker { get; init; } = "";
}

/// <summary>
/// The Alpaca routing opt-in (IP-T248, IP-LAW-3): <c>DefaultBroker</c> on the owner's settings
/// row. A key in Vault is never by itself permission to trade it; this switch is. Off (the
/// default) keeps every order on IdiotProof's Sandbox. Paper vs live is still chosen per strategy,
/// and a Live strategy still needs its own confirmation code.
/// </summary>
[Operation("keys opt-in", Summary = "Route your orders to your own Alpaca account (alpaca) or back to the Sandbox (off).", Risk = OperationRisk.Write)]
[Example("idiotproof keys opt-in alpaca")]
[Example("idiotproof keys opt-in off")]
public sealed class KeysOptInOperation : IOperation<KeysOptInArgs>
{
    public async Task<OperationResult> ExecuteAsync(KeysOptInArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var on = args.Broker.Trim().ToLowerInvariant() switch
        {
            "alpaca" or "on" => true,
            "off" or "sandbox" => false,
            _ => (bool?)null,
        };
        if (on is null) return OperationResult.Usage($"Unknown choice '{args.Broker}'. Use alpaca or off.");

        var service = ctx.Get<UserKeyService>();
        var keys = await service.GetOrCreateAsync(owner, ctx.CancellationToken);
        var was = string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase) ? "alpaca" : "sandbox";
        var now = on.Value ? "alpaca" : "sandbox";
        var hasPaper = keys.AlpacaApiKeyId is not null && keys.AlpacaApiSecretKey is not null;
        var hasLive = keys.AlpacaLiveApiKeyId is not null && keys.AlpacaLiveApiSecretKey is not null;
        var data = new { was, routing = now, changed = was != now, paper_keys = hasPaper, live_keys = hasLive };

        if (was == now)
            return OperationResult.Ok(data, $"Routing is already {now}.");

        // A Paper or Live strategy holding a position books its exits through this routing: switching
        // it would send the Monitor's stop/target/sell-by to the other broker and strand the real
        // position (the same rule as `strategy mode`). Sandbox-mode strategies never route to Alpaca.
        var holding = (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => s.PositionQty > 0 && IdiotProof.Core.Trading.OrderIntent.ParseMode(s.BrokerMode) != IdiotProof.Core.Trading.BrokerMode.Sandbox)
            .ToList();
        if (holding.Count > 0)
            return OperationResult.Blocked(
                $"{string.Join(", ", holding.Select(s => $"{s.Symbol} ({s.PositionQty} sh, {s.BrokerMode})"))} still hold positions; " +
                $"switching routing {was} -> {now} would strand them. Flatten first.");

        await service.SetDefaultBrokerAsync(owner, on.Value ? "alpaca" : "Sandbox", ctx.CancellationToken);
        var lines = new List<string> { $"Routing: {was} -> {now}." };
        if (on.Value)
        {
            lines.Add("Paper strategies trade your Alpaca paper account; Live strategies (each confirmed) trade your live account.");
            if (!hasPaper) lines.Add("No paper keys in Vault yet: `idiotproof keys set alpaca-paper --key-id PK... --secret ...`. Until then orders stay on the Sandbox.");
            if (!hasLive) lines.Add("No live keys in Vault (fine unless you switch a strategy to Live).");
        }
        else
        {
            lines.Add("Every order now goes to the Sandbox simulator. Your keys stay in Vault.");
        }
        return OperationResult.Ok(data, string.Join("\n", lines));
    }
}
