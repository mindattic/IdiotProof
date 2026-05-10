using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IdiotProof.Models;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// A user-authored trading strategy. Persisted to the IdiotProof SQL Server
/// database. The Monitor (StrategyExecutionService + future IdiotProof.Monitor
/// console app) loads every IsActive=true row, parses ScriptText into a
/// StrategyDefinition, and evaluates it on each tick.
/// </summary>
public sealed class Strategy
{
    /// <summary>
    /// Time-ordered identifier (UUIDv7). Newer rows sort after older rows
    /// when ordered by Id, which keeps grids stable without a separate sort key.
    /// </summary>
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Owning user (FK to AspNetUsers.Id). Indexed.
    /// </summary>
    [Required, MaxLength(450)]
    public string OwnerUserId { get; set; } = "";

    /// <summary>Display name shown on the Strategies list and tab title.</summary>
    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>Free-form prose description; the same text the user typed into the Describe tab.</summary>
    public string? Description { get; set; }

    /// <summary>Primary ticker the strategy monitors (e.g. "TSLA"). Indexed.</summary>
    [Required, MaxLength(20)]
    public string Symbol { get; set; } = "";

    /// <summary>Generated IdiotScript fluent text (e.g. Stock.Ticker("TSLA").IsAboveVwap()...).</summary>
    [Required]
    public string ScriptText { get; set; } = "";

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
}
