using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IdiotProof.Models;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// One row per executed trade lifecycle — the trade diary (IP-A23). Opened when
/// an entry order is placed, closed when the position exits, so every paper (or
/// sandbox / live) trade the Monitor makes is recorded end to end: side, size,
/// entry price/time, the full risk plan (stop, trailing stop, take-profit,
/// peak-giveback, sell-by), exit price/time/reason, and realized P&amp;L.
///
/// Deliberately DENORMALIZED (title/symbol/risk snapshot copied in) and NOT
/// foreign-keyed to <see cref="Strategy"/> with cascade: the diary is a
/// PERMANENT historical record and must survive the strategy being edited or
/// deleted. Runtime state → SQL (IP-LAW-7). Append-mostly: rows are inserted on
/// entry and updated once on exit; never deleted in normal operation.
/// </summary>
public sealed class TradeDiaryEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // ── Provenance ──────────────────────────────────────────────────────
    /// <summary>The strategy that produced this trade (plain column, no cascade).</summary>
    public Guid StrategyId { get; set; }

    /// <summary>Owning user (FK-free snapshot; the diary outlives account changes).</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Strategy title at trade time (snapshot).</summary>
    [Required, MaxLength(200)]
    public string StrategyTitle { get; set; } = "";

    [Required, MaxLength(20)]
    public string Symbol { get; set; } = "";

    /// <summary>Long / Short.</summary>
    [MaxLength(8)]
    public string Direction { get; set; } = nameof(TradeDirection.Long);

    // ── Where it executed ───────────────────────────────────────────────
    /// <summary>Broker the order routed to (Sandbox / Alpaca).</summary>
    [MaxLength(16)]
    public string Broker { get; set; } = "";

    /// <summary>
    /// True = simulated/paper (Sandbox always; Alpaca paper endpoint). False =
    /// REAL MONEY. Recorded straight from <c>IBrokerClient.IsPaper</c> so a live
    /// fill can never be mislabeled paper.
    /// </summary>
    public bool IsPaper { get; set; }

    // ── Entry (the buy) ─────────────────────────────────────────────────
    public DateTime EntryUtc { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal EntryPrice { get; set; }

    public int Quantity { get; set; }

    /// <summary>Dollar sizing when the strategy is notional (else null).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Notional { get; set; }

    /// <summary>Broker order id for the entry (reconciliation).</summary>
    [MaxLength(64)]
    public string? EntryOrderId { get; set; }

    // ── Risk plan at entry (snapshot of the rules managing this trade) ───
    [Column(TypeName = "decimal(18,4)")]
    public decimal? StopLossPrice { get; set; }

    [Column(TypeName = "decimal(9,4)")]
    public decimal? StopLossPercent { get; set; }

    [Column(TypeName = "decimal(9,4)")]
    public decimal? TrailingStopPercent { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? TakeProfitPrice { get; set; }

    [Column(TypeName = "decimal(9,4)")]
    public decimal? PeakGivebackPercent { get; set; }

    /// <summary>ET time the peak-giveback exit arms (HH:mm), if set.</summary>
    [MaxLength(8)]
    public string? PeakGivebackArmEt { get; set; }

    /// <summary>ET hard flatten time (HH:mm), if set.</summary>
    [MaxLength(8)]
    public string? SellByEt { get; set; }

    // ── Exit (the sell) ─────────────────────────────────────────────────
    public DateTime? ExitUtc { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? ExitPrice { get; set; }

    /// <summary>SellByTime / PeakGiveback / StopLoss / TrailingStop / TargetHit / NotFilled.</summary>
    [MaxLength(40)]
    public string? ExitReason { get; set; }

    [MaxLength(64)]
    public string? ExitOrderId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? RealizedPnL { get; set; }

    [Column(TypeName = "decimal(9,4)")]
    public decimal? ReturnPercent { get; set; }

    // ── Lifecycle ───────────────────────────────────────────────────────
    /// <summary>Open (holding), Closed (exited), NotFilled (entry never filled).</summary>
    [MaxLength(16)]
    public string Status { get; set; } = TradeDiaryStatus.Open;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Diary lifecycle states (kept as strings for forward-compat).</summary>
public static class TradeDiaryStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
    public const string NotFilled = "NotFilled";

    /// <summary>An Open row superseded by a newer trade on the same strategy
    /// (the Monitor stopped between a buy and its sell). Kept for the record.</summary>
    public const string Orphaned = "Orphaned";
}
