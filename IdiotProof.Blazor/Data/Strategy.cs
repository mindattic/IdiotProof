using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IdiotProof.Models;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// A user-authored trading strategy. Persisted to the IdiotProof SQL Server
/// database. The console Monitor (IdiotProof.Monitor/MonitorWorker) loads
/// every IsActive=true row each tick, parses ScriptText into a
/// StrategyDefinition, and evaluates it — so edits made in the Blazor UI
/// apply to the running console automatically via SQL (IP-A8).
/// </summary>
public sealed class Strategy
{
    /// <summary>
    /// Time-ordered identifier (UUIDv7). Newer rows sort after older rows
    /// when ordered by Id, which keeps grids stable without a separate sort key.
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning user (FK to auth.AuthUsers.Id). Indexed.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Display name shown on the Strategies list and tab title.</summary>
    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>Free-form prose description; the same text the user typed into the Describe tab.</summary>
    public string? Description { get; set; }

    /// <summary>Primary ticker the strategy monitors (e.g. "TSLA"). Indexed.</summary>
    [Required, MaxLength(20)]
    public string Symbol { get; set; } = "";

    /// <summary>
    /// IdiotScript fluent text — the HUMAN VIEW of the strategy (previews, the
    /// raw-script pane, hand editing). Since IP-A13 this is no longer what the
    /// evaluators run; <see cref="ScriptJson"/> is.
    /// </summary>
    [Required]
    public string ScriptText { get; set; } = "";

    /// <summary>
    /// The CANONICAL strategy (IP-LAW-8): versioned strict JSON of the full
    /// semantic model, written by <c>StrategyJson.Serialize</c>. Evaluators
    /// load this first and fail closed if they can't fully understand it.
    /// Null only on legacy rows written before the canon existed (backfilled
    /// at Blazor startup).
    /// </summary>
    public string? ScriptJson { get; set; }

    /// <summary>True = monitor + evaluate; False = paused.</summary>
    public bool IsActive { get; set; }

    /// <summary>Optional link to a Workspace this strategy belongs to.</summary>
    [MaxLength(200)]
    public string? WorkspaceId { get; set; }

    /// <summary>Bookkeeping.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Bookkeeping.</summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last time the Monitor evaluated this strategy and a signal fired (null until first hit).</summary>
    public DateTime? LastFiredUtc { get; set; }

    /// <summary>How many times this strategy has fired a TradeSignal since creation.</summary>
    public int FireCount { get; set; }

    // ── Open-position tracking (IP-A8) ──────────────────────────────────
    // Runtime state lives in SQL (IP-LAW-7). The Monitor writes these when an
    // entry order fills and clears them on exit; the UI renders live badges
    // from them. Qty > 0 means the Monitor is managing an open position and
    // must evaluate exit rules instead of entry conditions.

    /// <summary>Open position size in shares. 0 = flat.</summary>
    public int PositionQty { get; set; }

    /// <summary>Fill price of the open (or most recent) entry.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? LastEntryPrice { get; set; }

    /// <summary>When the open (or most recent) entry filled (UTC).</summary>
    public DateTime? EntryFilledUtc { get; set; }

    /// <summary>When the most recent exit completed (UTC). Null while holding.</summary>
    public DateTime? LastExitedUtc { get; set; }

    /// <summary>Fill price of the most recent exit.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? LastExitPrice { get; set; }

    /// <summary>Why the most recent exit happened (SellByTime / PeakGiveback / StopLoss / TrailingStop / Manual).</summary>
    [MaxLength(40)]
    public string? LastExitReason { get; set; }
}
