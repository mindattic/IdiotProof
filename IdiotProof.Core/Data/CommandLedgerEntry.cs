using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

/// <summary>
/// One row per operation the Hub ran, whichever front door asked (CLI, MCP, web): what was
/// called, by whom, with which arguments (secrets redacted before they get here), how it ended
/// and how long it took. The answer to "what did I (or the assistant) tell IdiotProof to do?".
/// Append-only, like <see cref="AuditLog"/>; trade-level detail stays in AuditLog/TradeDiary,
/// which link back through <see cref="Id"/> when an operation placed or changed something.
/// </summary>
public sealed class CommandLedgerEntry
{
    [Key]
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Owner the operation acted for (auth.AuthUsers.Id); null when none was resolved.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Catalog name, e.g. "strategy create".</summary>
    [Required, MaxLength(64)]
    public string Operation { get; set; } = "";

    /// <summary>"cli", "mcp", "blazor" or "test".</summary>
    [Required, MaxLength(16)]
    public string Caller { get; set; } = "";

    /// <summary>Arguments as JSON with secret-looking values replaced by SecretRedactor.</summary>
    public string? ArgsJson { get; set; }

    /// <summary>"Ok", "Failed", "UsageError", "Blocked" or "ConfirmationRequired".</summary>
    [Required, MaxLength(24)]
    public string Status { get; set; } = "";

    public int ExitCode { get; set; }

    /// <summary>Short outcome message (error text, veto reason); never a stack trace.</summary>
    [MaxLength(1024)]
    public string? Message { get; set; }

    public int DurationMs { get; set; }
}
