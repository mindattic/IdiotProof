using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

/// <summary>
/// One fade-short borrow pilot observation (IP-T480): a symbol whose premarket high reached +50% over the
/// adjusted prior close, what Alpaca's asset endpoint said about shorting it at 09:40 and 09:46 ET, the
/// advisory locate quote (GET /v1/locates/quotes, which reserves nothing), whether the fade-short signal
/// fired at 09:45, and the SSR / Reg SHO state. One row per (DateEt, Symbol). Research only: the pilot
/// never places, reserves or cancels anything. Times are UTC; operations show them in ET.
/// </summary>
public sealed class FadeShortBorrowObservation
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerUserId { get; set; }
    public DateOnly DateEt { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";

    /// <summary>Where the candidate came from: movers, formation, or both (comma separated).</summary>
    [MaxLength(40)] public string CandidateSource { get; set; } = "";

    public DateTime? Checked0940Utc { get; set; }
    public DateTime? Checked0946Utc { get; set; }
    public DateTime? SignalEvaluatedUtc { get; set; }

    public decimal? PrevClose { get; set; }
    public decimal? PmHigh { get; set; }
    /// <summary>Premarket high (04:00-09:29 bars) over the adjusted prior close, %.</summary>
    public double? PmHighPct { get; set; }
    /// <summary>Premarket high at or above +100%.</summary>
    public bool PmHigh100 { get; set; }
    public decimal? Open0930 { get; set; }
    public decimal? Close0945 { get; set; }
    /// <summary>Close as of 09:45 over the prior close, %.</summary>
    public double? GapAt0945 { get; set; }
    /// <summary>Close as of 09:45 over the 09:30 open, %.</summary>
    public double? ExtensionAt0945 { get; set; }
    public bool SignalFired { get; set; }

    public bool? Shortable0940 { get; set; }
    public bool? EasyToBorrow0940 { get; set; }
    public bool? Marginable0940 { get; set; }
    [MaxLength(16)] public string? Status0940 { get; set; }

    /// <summary>At 09:46 (the entry minute): the values the summary uses.</summary>
    public bool? Shortable { get; set; }
    public bool? EasyToBorrow { get; set; }
    public bool? Marginable { get; set; }
    [MaxLength(16)] public string? Status { get; set; }
    /// <summary>The asset's <c>borrow_status</c> when Alpaca sends one (easy_to_borrow / hard_to_borrow).</summary>
    [MaxLength(24)] public string? BorrowStatus { get; set; }

    /// <summary>True when the read-only locate quote endpoint answered for this symbol.</summary>
    public bool LocateQueried { get; set; }
    public bool? LocateAvailable0940 { get; set; }
    public double? LocateSize0940 { get; set; }
    public decimal? LocateFee0940 { get; set; }
    /// <summary>At 09:46: quoted shares available, null when not quoted.</summary>
    public bool? LocateAvailable { get; set; }
    public double? LocateSize { get; set; }
    /// <summary>Quoted locate fee per share (USD), advisory.</summary>
    public decimal? LocateFee { get; set; }
    /// <summary>"quoted", the quote's error code (e.g. easy_to_borrow), or why it was not queried.</summary>
    [MaxLength(200)] public string? LocateNote { get; set; }

    /// <summary>D-1 low over D-2 close, minus 1 (Rule 201 trigger at -10%).</summary>
    public double? SsrD1LowVsD2Close { get; set; }
    public bool? SsrActive { get; set; }
    /// <summary>On the D-1 Reg SHO threshold list; null when no D-1 list is on disk.</summary>
    public bool? RegSho { get; set; }

    [MaxLength(4000)] public string? RawJson { get; set; }
    [MaxLength(400)] public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
