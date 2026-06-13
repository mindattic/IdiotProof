using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Append-only audit trail for trade-relevant events: signal fires, order
/// placements, broker switches, risk-guardian vetoes. Written by the Monitor,
/// the Blazor app, and any future automation. Read by the audit page (future)
/// for compliance / debugging / "what fired and why" forensics.
///
/// Indexed on (Timestamp DESC) so the audit page reads recent-first without a
/// full-table scan, and on (UserId, Timestamp) so per-user queries are cheap.
/// Append-only: never UPDATE or DELETE rows in normal operation; archival
/// pruning is a separate maintenance job.
/// </summary>
public sealed class AuditLog
{
    [Key]
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>FK to auth.AuthUsers.Id when applicable; null for system-level events.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Coarse classification: "signal", "order", "broker", "risk", "system".</summary>
    [Required, MaxLength(32)]
    public string Category { get; set; } = "";

    /// <summary>Short human-readable summary.</summary>
    [Required, MaxLength(500)]
    public string Message { get; set; } = "";

    /// <summary>
    /// Optional structured payload (signal JSON, order request, broker response).
    /// Free-form; consumers parse based on Category.
    /// </summary>
    public string? DataJson { get; set; }
}
