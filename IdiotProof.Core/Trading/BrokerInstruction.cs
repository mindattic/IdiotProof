using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Core.Trading;

/// <summary>
/// A money-moving instruction on an Alpaca account that is not an order: exercise or
/// do-not-exercise a held option, withdraw crypto, add or remove a withdrawal whitelist entry.
/// Like an <see cref="OrderIntent"/>, it reaches Alpaca only through
/// <see cref="TransactionGateway.SubmitInstructionAsync"/> (IP-LAW-12): audited, linked to the
/// Hub's command ledger, and held for a confirmation code when Live (or always, for
/// <see cref="AlwaysConfirm"/> funding moves).
/// </summary>
public sealed record BrokerInstruction
{
    public required Guid OwnerUserId { get; init; }

    /// <summary>Paper or Live; Sandbox is the simulator and has no Alpaca account to instruct.</summary>
    public required BrokerMode Mode { get; init; }

    /// <summary>What it is, for people and the audit trail: "exercise", "do-not-exercise", "crypto withdraw".</summary>
    public required string Action { get; init; }

    /// <summary>POST, PATCH, PUT or DELETE.</summary>
    public required string Method { get; init; }

    /// <summary>The Trading API path, e.g. <c>/v2/positions/AAPL261219C00150000/exercise</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The exact JSON body sent (null for none).</summary>
    public string? BodyJson { get; init; }

    /// <summary>The size the person saw (contracts, coins), so a code never covers a different amount.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>The block the person sees before saying yes; its first line heads the audit row.</summary>
    public required string Preview { get; init; }

    /// <summary>Need a code on Paper too (crypto withdrawals and whitelist changes are irreversible).</summary>
    public bool AlwaysConfirm { get; init; }

    /// <summary>The AuditLog category for an executed or refused instruction ("order", "funding").</summary>
    public string AuditCategory { get; init; } = "order";

    /// <summary>Free-text reason kept in the audit trail; not part of what a code confirms.</summary>
    public string? Note { get; init; }

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The request a confirmation code is bound to: owner, account, action, verb, path, body and size.</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        owner = OwnerUserId,
        mode = Mode,
        action = Action,
        method = Method.Trim().ToUpperInvariant(),
        path = Path.Trim(),
        body = BodyJson,
        quantity = Quantity?.ToString("0.############################", CultureInfo.InvariantCulture),
    }, CanonicalJson);

    /// <summary>SHA-256 (hex) of <see cref="ToJson"/>: any change to the request needs its own code.</summary>
    public string Hash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson())));

    /// <summary>One line for logs: the preview's first line.</summary>
    public string Describe() => Preview.Split('\n')[0].TrimEnd('\r');
}

/// <summary>What happened to a <see cref="BrokerInstruction"/>.</summary>
public sealed record InstructionOutcome(GatewayStatus Status, BrokerInstruction Instruction, string Message)
{
    /// <summary>Alpaca's reply when it accepted the instruction (default for a 204).</summary>
    public JsonElement Response { get; init; }

    public string? ConfirmationCode { get; init; }
    public DateTime? ConfirmationExpiresUtc { get; init; }

    /// <summary>Alpaca's HTTP status, error code and message when it refused.</summary>
    public int? BrokerStatus { get; init; }
    public int? BrokerCode { get; init; }
    public string? BrokerMessage { get; init; }
}
