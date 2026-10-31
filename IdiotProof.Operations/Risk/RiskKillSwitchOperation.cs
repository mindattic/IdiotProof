using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;

namespace IdiotProof.Operations.Risk;

public sealed record RiskKillSwitchArgs
{
    [Argument(0, Name = "state", Description = "on (halt every new entry now) or off (resume; needs a confirmation code).")]
    public string State { get; init; } = "";
}

/// <summary>
/// The owner's kill switch. <c>on</c> takes effect at once and needs nothing: the Risk Guardian
/// vetoes every new entry, from the Monitor or by hand, while exits still go through so a position
/// can always be closed (IP-LAW-1). <c>off</c> re-opens the door, so it asks first: the first call
/// returns a single-use code, the same call with <c>--confirm &lt;code&gt;</c> turns it off.
/// </summary>
[Operation("risk kill-switch", Summary = "Halt every new entry now (on), or resume (off, which needs a confirmation code); exits always go through.", Risk = OperationRisk.Write)]
[Example("idiotproof risk kill-switch on")]
[Example("idiotproof risk kill-switch off", Description = "Returns a code; re-run with --confirm <code> to resume entries.")]
public sealed class RiskKillSwitchOperation : IOperation<RiskKillSwitchArgs>
{
    /// <summary>The ConfirmationStore kind for turning the kill switch off.</summary>
    public const string ConfirmKind = "kill-switch-off";

    public async Task<OperationResult> ExecuteAsync(RiskKillSwitchArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var state = args.State.Trim().ToLowerInvariant();
        if (state is not ("on" or "off")) return OperationResult.Usage("Say on or off, e.g. `idiotproof risk kill-switch on`.");
        var guardians = ctx.Get<RiskGuardianService>();
        var isOn = await guardians.IsKillSwitchOnAsync(owner, ctx.CancellationToken);

        if (state == "on")
        {
            await guardians.SetKillSwitchAsync(owner, true, ctx.CancellationToken);
            return OperationResult.Ok(new { killSwitch = true, wasOn = isOn },
                (isOn ? "The kill switch was already on." : "Kill switch ON.") +
                " Every new entry is vetoed by the Risk Guardian until `idiotproof risk kill-switch off`; exits still go through.");
        }

        if (!isOn) return OperationResult.Ok(new { killSwitch = false, wasOn = false }, "The kill switch is already off.");

        var payload = JsonSerializer.Serialize(new { owner, action = ConfirmKind });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            const string preview = "TURN OFF the kill switch: the Monitor and manual orders may open new positions again.";
            var pending = await store.IssueAsync(owner, ConfirmKind, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, new { killSwitch = true });
        }

        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmKind && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        if (!redemption.Succeeded) return OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });

        await guardians.SetKillSwitchAsync(owner, false, ctx.CancellationToken);
        return OperationResult.Ok(new { killSwitch = false, wasOn = true }, "Kill switch OFF. New entries are allowed again, within your limits (`idiotproof risk status`).");
    }
}
