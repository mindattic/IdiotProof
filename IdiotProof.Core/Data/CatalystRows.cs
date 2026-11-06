using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Core.Data;

/// <summary>
/// One news item or SEC filing as the catalyst intake saw it (IP-T443): when the source published
/// it, when this process received it (the latency the gapper study measures), the deterministic
/// pre-class the rule tables assign, the optional Haiku classification, and the 0-100 catalyst
/// score. Research rows, never trading state: the Monitor and the formation recorder only read them.
/// </summary>
public sealed class CatalystEvent
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>"alpaca-news" | "edgar".</summary>
    [MaxLength(32)] public string Source { get; set; } = "";

    /// <summary>The source's own id: Alpaca's numeric news id, EDGAR's accession number.</summary>
    [MaxLength(64)] public string ExternalId { get; set; } = "";

    public DateTime PublishedUtc { get; set; }
    public DateTime ReceivedUtc { get; set; }

    [MaxLength(16)] public string PrimarySymbol { get; set; } = "";

    /// <summary>JSON string[] of every symbol the item names.</summary>
    [MaxLength(2000)] public string SymbolsJson { get; set; } = "[]";

    [MaxLength(500)] public string Headline { get; set; } = "";
    [MaxLength(1000)] public string? Url { get; set; }

    /// <summary>EDGAR form type (8-K, 4, S-1, 424B4); null for news.</summary>
    [MaxLength(16)] public string? FormType { get; set; }

    /// <summary>JSON string[] of 8-K item codes; null when unknown.</summary>
    [MaxLength(200)] public string? ItemsJson { get; set; }

    /// <summary>1 primary (EDGAR), 2 editorial, 3 wire/promotional.</summary>
    public int SourceTier { get; set; } = 3;

    /// <summary>The item's own source name as the feed gave it ("benzinga", "fiscal.ai"); null for older rows.</summary>
    [MaxLength(64)] public string? SourceName { get; set; }

    /// <summary>A <see cref="IdiotProof.Core.Catalysts.CatalystStampKind"/>: realtime | batch (PublishedUtc is an ingestion upper bound) | unknown.</summary>
    [MaxLength(12)] public string StampKind { get; set; } = "unknown";

    /// <summary>A <see cref="IdiotProof.Core.Catalysts.CatalystPreClass"/> name.</summary>
    [MaxLength(24)] public string PreClass { get; set; } = "Other";
    public int PreDirection { get; set; }
    public double PreConfidence { get; set; }

    [MaxLength(24)] public string? LlmType { get; set; }
    [MaxLength(10)] public string? LlmSentiment { get; set; }
    [MaxLength(10)] public string? LlmMagnitude { get; set; }
    public bool? LlmHasHappened { get; set; }
    [MaxLength(500)] public string? LlmMechanism { get; set; }
    public DateTime? LlmClassifiedUtc { get; set; }

    /// <summary>This symbol's earlier events inside the novelty window at publish time.</summary>
    public int NoveltyCount { get; set; }

    public double CatalystScore { get; set; }
    public int Direction { get; set; }
    public DateTime ScoreComputedUtc { get; set; }

    public string? RawSnippet { get; set; }

    /// <summary>True when written by the backfill (ReceivedUtc == PublishedUtc, so latency is unknown).</summary>
    public bool Backfilled { get; set; }
}

/// <summary>One (event, symbol) pair so every symbol an item names is queryable, not only the primary.</summary>
public sealed class CatalystEventSymbol
{
    public Guid EventId { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
}
