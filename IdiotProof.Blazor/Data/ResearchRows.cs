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
