using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

// IP-T493 "Momentum." claim tracker: the @trade.momentum YouTube shorts, their caption transcripts, the
// claims extracted from them and each claim checked against real Alpaca 1-minute SIP bars. Research only:
// nothing here is read by the trading path. Times are UTC; operations and the report show them in ET.

/// <summary>One kept short from the channel (title starts with TOP/Top or carries a date).</summary>
public sealed class MomentumVideo
{
    [Key, MaxLength(32)] public string VideoId { get; set; } = "";
    [MaxLength(200)] public string Url { get; set; } = "";
    [MaxLength(300)] public string Title { get; set; } = "";
    public DateTime? PublishedUtc { get; set; }
    /// <summary>The date written in the title (11/9/26), when there is one.</summary>
    public DateOnly? TitleDate { get; set; }
    /// <summary>The session the video is about: the title date, else the collector's inference from the upload time.</summary>
    public DateOnly? TradeDate { get; set; }
    /// <summary>watchlist (evening "TOP 3 STOCKS"), morning-update ("TOP 3 STOCKS UPDATE") or other.</summary>
    [MaxLength(24)] public string Kind { get; set; } = "other";
    public int? DurationSec { get; set; }
    /// <summary>Position in the channel's shorts list when collected (1 = newest).</summary>
    public int? ChannelIndex { get; set; }
    public DateTime? FetchedUtc { get; set; }
    public DateTime ImportedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A video's caption transcript, stored by trade day. One row per video.</summary>
public sealed class MomentumTranscript
{
    [Key, MaxLength(32)] public string VideoId { get; set; } = "";
    public DateOnly? TradeDate { get; set; }
    public string? Text { get; set; }
    public string? SegmentsJson { get; set; }
    [MaxLength(40)] public string Source { get; set; } = "youtube-captions";
    /// <summary>The collector's error when no captions were read (rate limit, disabled captions).</summary>
    [MaxLength(400)] public string? Error { get; set; }
    /// <summary>SHA-256 of <see cref="Text"/>: a changed transcript is re-extracted.</summary>
    [MaxLength(64)] public string? TextHash { get; set; }
    /// <summary>When claims were last extracted from this text (null = not yet).</summary>
    public DateTime? ExtractedUtc { get; set; }
    [MaxLength(100)] public string? ExtractedBy { get; set; }
    [MaxLength(400)] public string? ExtractError { get; set; }
    public DateTime ImportedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// One claim from a transcript: a ranked pick with its chart-level scenarios (watchlist-scenario,
/// morning-update-scenario), a past-performance brag ("PARA squeezed 400%") or a market statement.
/// </summary>
public sealed class MomentumClaim
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    [MaxLength(32)] public string VideoId { get; set; } = "";
    public DateOnly TradeDate { get; set; }
    /// <summary>1-3 for a pick in the order spoken; null for brags and market statements.</summary>
    public int? Rank { get; set; }
    /// <summary>watchlist-scenario, morning-update-scenario, past-performance or market-statement.</summary>
    [MaxLength(32)] public string ClaimType { get; set; } = "";
    /// <summary>The ticker as the captions carry it ("P A R A", "CHS"), or null when he never said it.</summary>
    [MaxLength(40)] public string? SpokenSymbol { get; set; }
    /// <summary>The ticker validated against Alpaca assets and that day's bars; null when unresolved.</summary>
    [MaxLength(16)] public string? Symbol { get; set; }
    /// <summary>0-1: exact spoken match on a mover scores high, a guess from the description alone low.</summary>
    public double? SymbolConfidence { get; set; }
    /// <summary>How the symbol was resolved (exact, prefix, one-letter fix, identified from the description).</summary>
    [MaxLength(400)] public string? SymbolNote { get; set; }
    /// <summary>What he said about the stock instead of (or beside) the ticker: "closed up over 80% into the weekend".</summary>
    [MaxLength(400)] public string? IdentityHint { get; set; }
    /// <summary>A move he quoted for the stock (+80%), percent.</summary>
    public double? StatedMovePct { get; set; }
    /// <summary>JSON array of setup tags (day-2-continuation, flush-higher-low, vwap-reclaim, wedge-breakout, ...).</summary>
    [MaxLength(1000)] public string? SetupTags { get; set; }
    [MaxLength(8)] public string Direction { get; set; } = "long";
    /// <summary>JSON array of scenarios: trigger kind and level, confirm levels, targets, invalidation, each level as spoken and resolved.</summary>
    public string? Scenarios { get; set; }
    /// <summary>The price the spoken levels were scaled against (prior close, or the premarket price for a morning update).</summary>
    public decimal? ReferencePrice { get; set; }
    /// <summary>Brag: the claimed percent move.</summary>
    public double? ClaimedPct { get; set; }
    /// <summary>Brag: the window it covers (previous-session, last-week, this-week, recent).</summary>
    [MaxLength(40)] public string? ClaimWindow { get; set; }
    public string? RawQuote { get; set; }
    [MaxLength(100)] public string ExtractedBy { get; set; } = "";
    public DateTime ExtractedUtc { get; set; }
}

/// <summary>
/// One claim scenario checked against Alpaca 1-minute SIP bars (04:00-20:00 ET) on the trade date. One row
/// per (claim, scenario); brags and market statements use scenario 0.
/// </summary>
public sealed class MomentumClaimOutcome
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClaimId { get; set; }
    public int ScenarioIndex { get; set; }
    public DateTime VerifiedUtc { get; set; }
    [MaxLength(60)] public string DataSource { get; set; } = "";

    public decimal? PriorClose { get; set; }
    public decimal? Open0930 { get; set; }
    /// <summary>09:30 open over the split-consistent prior close, percent.</summary>
    public double? GapPct0930 { get; set; }
    public decimal? PremarketHigh { get; set; }
    public decimal? DayHigh { get; set; }
    public decimal? DayClose { get; set; }

    [MaxLength(16)] public string? TriggerKind { get; set; }
    public decimal? TriggerLevel { get; set; }
    public bool TriggerHit { get; set; }
    public DateTime? TriggerUtc { get; set; }
    public decimal? TriggerPrice { get; set; }
    public bool? ConfirmHit { get; set; }
    public decimal? ConfirmLevel { get; set; }
    public DateTime? ConfirmUtc { get; set; }
    public decimal? EntryPrice { get; set; }
    public DateTime? EntryUtc { get; set; }

    /// <summary>JSON array of {level, hitUtc} for every stated target above the trigger, ascending.</summary>
    [MaxLength(2000)] public string? TargetsJson { get; set; }
    public int TargetsHit { get; set; }
    public bool FirstTargetHit { get; set; }
    public DateTime? FirstTargetUtc { get; set; }
    public bool AnyTargetHit { get; set; }
    public decimal? InvalidationLevel { get; set; }
    /// <summary>stated, default-confirm, default-trigger or default-vwap.</summary>
    [MaxLength(24)] public string? InvalidationSource { get; set; }
    public bool InvalidationHitBeforeTarget { get; set; }

    public double? MaxRunAfterTriggerPct { get; set; }
    public double? MaxDrawdownAfterTriggerPct { get; set; }
    public double? CloseVsTriggerPct { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateTime? ExitUtc { get; set; }
    /// <summary>target, stop, time (15:55 ET), no-confirm (never entered).</summary>
    [MaxLength(16)] public string? ExitReason { get; set; }
    public double? PlanPnlPct { get; set; }
    /// <summary>target-hit, triggered-no-target, stopped, not-triggered, unverifiable; brags: brag-accurate, brag-exaggerated; market statements: not-evaluated.</summary>
    [MaxLength(32)] public string Verdict { get; set; } = "";

    /// <summary>Rank among the day's gainers by high over prior close (liquid names, prior close >= $0.10).</summary>
    public int? GainerRank { get; set; }
    public int? GainerCount { get; set; }
    public int? GapRank { get; set; }
    public double? DayHighPct { get; set; }
    public double? DayClosePct { get; set; }
    /// <summary>Rank among the previous session's gainers by close (the move his evening watchlist reacts to).</summary>
    public int? PrevDayGainerRank { get; set; }
    public double? PrevDayClosePct { get; set; }
    /// <summary>Gainer rank on the trade date at or under 20.</summary>
    public bool? TopGapper { get; set; }

    public double? D1MaxRunPct { get; set; }
    public bool? D1AnyTargetHit { get; set; }

    /// <summary>Brag: the actual move over the window from the prior close to the highest high, and to the last close.</summary>
    public double? BragActualHighPct { get; set; }
    public double? BragActualClosePct { get; set; }
    /// <summary>Brag: the actual move from the window's lowest low to the later highest high.</summary>
    public double? BragActualLowToHighPct { get; set; }

    [MaxLength(1000)] public string? Notes { get; set; }
}
