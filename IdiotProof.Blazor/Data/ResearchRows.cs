using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// A single catalyst or portent extracted from a news article or primary source.
/// HasHappenedAlready = false means this is a pending portent — an announced
/// event whose price impact has not yet materialised. These are the most
/// valuable signals: they represent future catalysts the market may not yet
/// have fully priced.
/// </summary>
public sealed class ResearchClaim
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(20)]  public string Ticker     { get; set; } = "";
    [MaxLength(200)] public string SourceName { get; set; } = "";
    [MaxLength(500)] public string? SourceUrl { get; set; }

    /// <summary>1 = Primary (EDGAR/Gov), 2 = Editorial, 3 = Promotional/Unknown</summary>
    public int SourceTier { get; set; } = 3;

    public DateOnly ArticleDate { get; set; }

    [MaxLength(500)] public string ClaimSummary { get; set; } = "";

    /// <summary>Earnings | Contract | Insider | MA | Guidance | Regulatory | News</summary>
    [MaxLength(50)] public string ClaimType { get; set; } = "News";

    /// <summary>Bullish | Bearish | Neutral</summary>
    [MaxLength(10)] public string Sentiment { get; set; } = "Neutral";

    /// <summary>High | Medium | Low</summary>
    [MaxLength(10)] public string Magnitude { get; set; } = "Low";

    /// <summary>
    /// False = portent: announced but not yet executed (e.g. "preliminary contract
    /// pending signature", "acquisition announced, closing Q4 2026").
    /// True = immediate: the event has already occurred.
    /// </summary>
    public bool HasHappenedAlready { get; set; }

    [MaxLength(300)] public string? PendingTrigger    { get; set; }
    [MaxLength(50)]  public string? ExpectedTimeline  { get; set; }

    /// <summary>High | Medium | Low — LLM confidence the portent will materialise</summary>
    [MaxLength(10)]  public string? TriggerConfidence { get; set; }

    /// <summary>Pending | Realized | Expired | Disproven</summary>
    [MaxLength(20)]  public string Status { get; set; } = "Pending";

    [MaxLength(2000)] public string? LlmAnswer { get; set; }

    public decimal? PriceAtClaim    { get; set; }
    public decimal? PriceAtOutcome  { get; set; }
    public DateOnly? OutcomeDate    { get; set; }
    public decimal? OutcomePctChange { get; set; }

    public string? RawArticleSnippet { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True for a macro/regulatory event (e.g. an exchange listing-rule change)
    /// that isn't about one company — <see cref="Ticker"/> is blank and the
    /// tickers it plausibly affects live in <see cref="AffectedTickersJson"/>.
    /// </summary>
    public bool IsMacro { get; set; }

    /// <summary>
    /// JSON string[] of tickers affected by a macro claim, or a descriptive
    /// count string (e.g. "~340 Nasdaq Capital Market issuers") when the
    /// affected set is too large/uncertain to enumerate.
    /// </summary>
    public string? AffectedTickersJson { get; set; }

    /// <summary>
    /// 0-100 computed by <c>SignificanceScorer</c> — combines LLM magnitude/
    /// confidence, historical correlation strength, source trust, recency, and
    /// watchlist membership. Null until the scanner has scored the claim.
    /// Drives the ranked-feed sort order on the Research tab.
    /// </summary>
    public double? SignificanceScore { get; set; }
}

/// <summary>
/// Structured Form 4 non-derivative transaction data parsed from the actual
/// filing XML — real share counts and dollar values, not filing-metadata
/// boilerplate. One row per transaction line (a single Form 4 can report
/// several); each links back to the <see cref="ResearchClaim"/> it fed.
/// </summary>
public sealed class InsiderTransaction
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClaimId { get; set; }

    [MaxLength(200)] public string FilerName { get; set; } = "";

    /// <summary>Officer | Director | TenPercentOwner | Other</summary>
    [MaxLength(20)] public string FilerRole { get; set; } = "Other";

    /// <summary>SEC transaction code: S=sale, P=purchase, A=grant/award, M=option exercise, F=tax withholding, G=gift, etc.</summary>
    [MaxLength(5)] public string TransactionCode { get; set; } = "";

    public DateOnly TransactionDate { get; set; }

    public decimal SharesTransacted { get; set; }

    /// <summary>Null when the filing reports no cash price (e.g. a gift or certain option exercises).</summary>
    public decimal? PricePerShare { get; set; }

    public decimal? DollarValue { get; set; }

    public decimal SharesOwnedAfter { get; set; }

    /// <summary>Positive = shares owned increased (acquired); negative = decreased (disposed).</summary>
    public decimal? PctOfHoldingsChanged { get; set; }

    [MaxLength(500)] public string? FilingUrl { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cached snapshot of one tradable US equity, refreshed daily from Alpaca's
/// asset list. The market-sweep's ticker universe, and the best-effort
/// price × shares-outstanding screen the regulatory scanner uses to guess
/// which issuers a rule change (e.g. a market-value listing threshold)
/// plausibly affects.
/// </summary>
public sealed class TrackedTicker
{
    [Key, MaxLength(20)] public string Symbol { get; set; } = "";

    [MaxLength(20)] public string Exchange { get; set; } = "";

    public bool IsTradable { get; set; }

    public decimal? LastPrice { get; set; }

    /// <summary>Best-effort, from EDGAR company facts XBRL; null when unavailable.</summary>
    public long? SharesOutstanding { get; set; }

    public DateTime LastRefreshedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per <c>IdiotProof.ResearchScanner</c> execution — observability for
/// the unattended scheduled-task scan, so the Research tab can show "last
/// scanned Xm ago, covered N/M tracked tickers" instead of silently capping
/// coverage with no visibility.
/// </summary>
public sealed class ScanRun
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedUtc { get; set; }

    public int TickersScanned { get; set; }

    public int UniverseSize { get; set; }

    public int ClaimsFound { get; set; }

    public int ErrorCount { get; set; }

    [MaxLength(2000)] public string? Notes { get; set; }
}

/// <summary>
/// Per-source empirical accuracy score. Updated by the outcome backtester as
/// portents are confirmed or disproven against actual price moves. Sources
/// accumulate trust over time — the system learns which outlets surface real
/// signals vs. promotional noise.
/// </summary>
public sealed class SourceTrustScore
{
    [Key, MaxLength(200)] public string SourceName { get; set; } = "";

    public int SourceTier       { get; set; }
    public int TotalClaims      { get; set; }
    public int PortentsClaimed  { get; set; }
    public int PortentsRealized { get; set; }
    public int ImmediateClaims  { get; set; }
    public int ImmediateCorrect { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public double? ConfidencePct =>
        TotalClaims > 0
            ? (double)(PortentsRealized + ImmediateCorrect) / TotalClaims * 100
            : null;
}

/// <summary>
/// 20-dimensional LLM-scored feature vector + 64-bit LSH signature for a claim.
/// Enables approximate nearest-neighbour search across historical claims to surface
/// "when signals that look like this appeared in the past, what happened?"
///
/// The 20 dimensions cover: revenue_impact, market_share, regulatory_dependency,
/// timeline_certainty, insider_conviction, government_nexus, sector_spillover,
/// ma_probability, urgency, capex_event, product_pipeline, competitive_position,
/// execution_risk, macro_lever, momentum_catalyst, source_credibility,
/// dollar_materiality, sentiment_surprise, volatility_setup, portent_quality.
/// </summary>
public sealed class ResearchClaimVector
{
    [Key]
    public Guid ClaimId { get; set; }

    /// <summary>JSON-serialised float[20] feature vector produced by Claude Haiku.</summary>
    public string VectorJson { get; set; } = "[]";

    /// <summary>64-bit LSH signature (8 bytes). Hamming distance &lt;= 20 → similar claim.</summary>
    public byte[] LshSignature { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
