using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Ledger;

namespace IdiotProof.Core.Trading;

/// <summary>
/// The gateway's path for instructions that move money or positions without being orders
/// (<see cref="BrokerInstruction"/>: option exercise / do-not-exercise, crypto withdrawals and
/// whitelist changes). Same rules as an order: an Alpaca account only, Live (or any
/// <see cref="BrokerInstruction.AlwaysConfirm"/> move) waits for a code bound to the exact request,
/// the call to Alpaca is made here and nowhere else, and every outcome leaves an AuditLog row
/// linked to the Hub's CommandLedger row through <see cref="CommandScope"/>.
/// </summary>
public sealed partial class TransactionGateway
{
    /// <summary>The <see cref="ConfirmationStore"/> kind an instruction's code is issued under.</summary>
    public const string KindInstruction = "instruction";

    private static readonly string[] InstructionMethods = ["POST", "PATCH", "PUT", "DELETE"];

    /// <summary>
    /// Runs <paramref name="instruction"/>. Without <paramref name="confirmationCode"/>, a Live or
    /// always-confirm instruction is previewed and a code issued (nothing is sent); with the code,
    /// the same instruction is sent once. A code issued for anything else is refused and not used up.
    /// </summary>
    public async Task<InstructionOutcome> SubmitInstructionAsync(BrokerInstruction instruction, string caller,
        string? confirmationCode = null, CancellationToken ct = default)
    {
        InstructionOutcome Stop(GatewayStatus status, string message) => new(status, instruction, message);

        // 1 ── the instruction makes sense on its own
        var method = instruction.Method.Trim().ToUpperInvariant();
        if (instruction.Mode == BrokerMode.Sandbox)
            return await AuditInstructionAsync(Stop(GatewayStatus.Blocked,
                $"Sandbox is IdiotProof's simulator and has no Alpaca account to {instruction.Action} on; use paper or live."), caller, null, ct);
        if (!InstructionMethods.Contains(method) || !instruction.Path.StartsWith('/') || instruction.Path.Contains("://", StringComparison.Ordinal))
            return await AuditInstructionAsync(Stop(GatewayStatus.Blocked, $"Malformed instruction: {method} {instruction.Path}."), caller, null, ct);
        if (alpaca is null)
            return await AuditInstructionAsync(Stop(GatewayStatus.Blocked, "No Alpaca access is configured for the gateway."), caller, null, ct);

        // 2 ── real money (or an irreversible funding move) waits for the person's explicit yes
        string? confirmedBy = null;
        if (instruction.Mode == BrokerMode.Live || instruction.AlwaysConfirm)
        {
            var hash = instruction.Hash();
            if (string.IsNullOrWhiteSpace(confirmationCode))
            {
                var pending = await confirmations.IssueAsync(instruction.OwnerUserId, KindInstruction,
                    instruction.ToJson(), hash, instruction.Preview, ct);
                return await AuditInstructionAsync(Stop(GatewayStatus.ConfirmationRequired,
                    $"{instruction.Mode} {instruction.Action} needs confirmation: code {pending.Code} (valid {ConfirmationStore.Lifetime.TotalSeconds:0}s).") with
                {
                    ConfirmationCode = pending.Code,
                    ConfirmationExpiresUtc = pending.ExpiresUtc,
                }, caller, null, ct);
            }
            var payload = instruction.ToJson();
            var redemption = await confirmations.RedeemAsync(confirmationCode, instruction.OwnerUserId, caller,
                p => p.Kind == KindInstruction && p.PayloadJson == payload ? hash : "mismatch", ct);
            if (!redemption.Succeeded)
                return await AuditInstructionAsync(Stop(GatewayStatus.Blocked, redemption.Message), caller, null, ct);
            confirmedBy = "code";
        }

        // 3 ── send it to the owner's Alpaca account
        try
        {
            await using var api = await alpaca.ForOwnerAsync(instruction.OwnerUserId, instruction.Mode, ct);
            object? body = instruction.BodyJson is { Length: > 0 } json ? JsonDocument.Parse(json).RootElement.Clone() : null;
            var response = await api.SendAsync(new HttpMethod(method), instruction.Path, body, null, ct);
            return await AuditInstructionAsync(Stop(GatewayStatus.Placed, $"Alpaca {instruction.Mode} accepted {instruction.Action}.") with
            {
                Response = response,
            }, caller, confirmedBy, ct);
        }
        catch (AlpacaAccessException ex)
        {
            return await AuditInstructionAsync(Stop(GatewayStatus.Blocked, ex.Message), caller, confirmedBy, ct);
        }
        catch (AlpacaApiException ex)
        {
            return await AuditInstructionAsync(Stop(GatewayStatus.Rejected, ex.AlpacaMessage) with
            {
                BrokerStatus = (int)ex.Status,
                BrokerCode = ex.AlpacaCode,
                BrokerMessage = ex.AlpacaMessage,
            }, caller, confirmedBy, ct);
        }
    }

    /// <summary>Every instruction outcome leaves an audit row ("Placed: EXERCISE 2 … — message"), linked to the ledger.</summary>
    private async Task<InstructionOutcome> AuditInstructionAsync(InstructionOutcome outcome, string caller, string? confirmedBy, CancellationToken ct)
    {
        var i = outcome.Instruction;
        try
        {
            var data = JsonSerializer.Serialize(new
            {
                status = outcome.Status.ToString(),
                caller,
                instruction = JsonDocument.Parse(i.ToJson()).RootElement,
                note = i.Note,
                broker_status = outcome.BrokerStatus,
                broker_message = outcome.BrokerMessage,
                code_issued = outcome.ConfirmationCode is not null,
                live_confirmed_by = confirmedBy,
                command = CommandInfo(),
            });
            var category = outcome.Status is GatewayStatus.Placed or GatewayStatus.Rejected ? i.AuditCategory : "risk";
            var id = await audit.LogReturningIdAsync(category, $"{outcome.Status}: {i.Describe()} — {outcome.Message}", i.OwnerUserId, data, ct);
            if (id is { } auditId) CommandScope.Current?.LinkAudit(auditId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Audit write failed for {Instruction} (continuing).", i.Describe());
        }
        return outcome;
    }
}
