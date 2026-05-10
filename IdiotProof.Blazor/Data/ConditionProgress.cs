using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Per-strategy snapshot of how far along the entry-condition chain the
/// Monitor got on its most recent evaluation. One row per Strategy
/// (StrategyId is the primary key) — upserted every tick. Cleared when the
/// strategy fires (PassedCount = TotalCount).
///
/// Used by the Strategies page to render a live "3/5 — waiting on OnReclaim(9)"
/// badge on active rows so the user sees evaluation status without tailing
/// the Monitor's stdout. Polling cadence is up to the page (currently
/// every few seconds via SignalR/timer).
///
/// We keep this as a separate single-row table rather than columnizing onto
/// the Strategy itself so writes are isolated — the Monitor hammers this
/// table on every tick; isolating it keeps the Strategies row's UpdatedUtc
/// stable (which the Strategies-page sort relies on).
/// </summary>
public sealed class ConditionProgress
{
    [Key]
    public Guid StrategyId { get; set; }

    /// <summary>Number of entry conditions that passed in the most recent evaluation.</summary>
    public int PassedCount { get; set; }

    /// <summary>Total entry conditions on this strategy.</summary>
    public int TotalCount { get; set; }

    /// <summary>The first condition that failed, in script form (e.g. "IsOnReclaim(9)"). Null when all passed.</summary>
    [MaxLength(280)]
    public string? FirstFailingVerb { get; set; }

    public DateTime EvaluatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True when every entry condition passed on the most recent evaluation.</summary>
    public bool IsFullPass => TotalCount > 0 && PassedCount == TotalCount;
}
