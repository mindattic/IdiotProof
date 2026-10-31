namespace IdiotProof.Core.Trading;

/// <summary>
/// The confirm-code loop for a one-shot console tool (the Monitor operator CLI's flatten and
/// test-order). Submits through the <see cref="TransactionGateway"/>; when Live comes back
/// <see cref="GatewayStatus.ConfirmationRequired"/>, prints the preview and the code and reads
/// the code back from the operator — the same preview → code → confirm law as the Hub's CLI/MCP,
/// just within one process. No code typed (or no console) means nothing is placed.
/// </summary>
public static class GatewayConsole
{
    /// <summary>
    /// Submits <paramref name="intent"/>; on a Live code, asks <paramref name="readCode"/> for it
    /// (null/blank = the operator declined) and confirms exactly that intent. Returns the final
    /// outcome; a refused or declined code returns the ConfirmationRequired outcome unchanged
    /// with its message replaced by why nothing happened.
    /// </summary>
    public static async Task<GatewayOutcome> SubmitAsync(
        TransactionGateway gateway, OrderIntent intent, string caller,
        Func<string?> readCode, Action<string> write, CancellationToken ct = default)
    {
        var outcome = await gateway.SubmitAsync(intent, new GatewayOptions { Caller = caller }, ct);
        if (outcome.Status != GatewayStatus.ConfirmationRequired) return outcome;

        write(outcome.Preview());
        write($"LIVE — real money. Type the code {outcome.ConfirmationCode} to confirm (valid {ConfirmationStore.Lifetime.TotalSeconds:0}s), or press Enter to cancel:");
        var typed = readCode()?.Trim();
        if (string.IsNullOrEmpty(typed))
            return outcome with { Message = "Not confirmed — nothing was sent." };

        try
        {
            return await gateway.ConfirmAsync(typed, intent.OwnerUserId, caller, intent.Hash(), ct)
                ?? outcome with { Message = "That code was not for an order — nothing was sent." };
        }
        catch (ConfirmationRefusedException ex)
        {
            return outcome with { Message = ex.Message + " Nothing was sent." };
        }
    }

    /// <summary>Reads one line from the console, or null when input is redirected/closed (never blocks a service).</summary>
    public static string? ReadLineIfInteractive() =>
        Console.IsInputRedirected ? null : Console.ReadLine();
}
