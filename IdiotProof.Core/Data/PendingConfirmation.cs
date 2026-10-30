using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// A Live (real-money) action waiting for its human "yes". The Hub stores the exact
/// <c>OrderIntent</c> (or strategy activation) and hands back a short code; only a second call
/// presenting that code, by the same owner, before <see cref="ExpiresUtc"/>, executes it — once.
/// Rows are kept after use as the record of who confirmed what.
/// </summary>
public sealed class PendingConfirmation
{
    [Key]
    public long Id { get; set; }

    /// <summary>Six characters from an unambiguous alphabet (no 0/O, 1/I/L).</summary>
    [Required, MaxLength(8)]
    public string Code { get; set; } = "";

    public Guid OwnerUserId { get; set; }

    /// <summary>"order" or "activate-live"; tells the confirm step how to replay <see cref="PayloadJson"/>.</summary>
    [Required, MaxLength(32)]
    public string Kind { get; set; } = "";

    /// <summary>SHA-256 of the payload's canonical form at issue time; re-checked before executing.</summary>
    [Required, MaxLength(64)]
    public string PayloadHash { get; set; } = "";

    /// <summary>The exact thing to execute (an OrderIntent's JSON, or the strategy id to activate).</summary>
    [Required]
    public string PayloadJson { get; set; } = "";

    /// <summary>What the user was shown (one-line summary + risk numbers).</summary>
    [MaxLength(2048)]
    public string? Preview { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }

    /// <summary>Set once, atomically, when the code is used; a concurrency token so two confirms can't both win.</summary>
    [ConcurrencyCheck]
    public DateTime? ConsumedUtc { get; set; }

    /// <summary>"cli", "mcp" or "blazor" — who presented the code.</summary>
    [MaxLength(16)]
    public string? ConsumedBy { get; set; }
}
