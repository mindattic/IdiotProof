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

    /// <summary>
    /// A <see cref="IdiotProof.Core.Catalysts.CatalystLegacyClass"/> name: the version 1 class, frozen with the gapper
    /// study (amendment 08). The score, <see cref="PreDirection"/> and <see cref="PreConfidence"/> come from the same rules.
    /// </summary>
    [MaxLength(24)] public string PreClass { get; set; } = "Other";
    public int PreDirection { get; set; }
    public double PreConfidence { get; set; }

    /// <summary>A <see cref="IdiotProof.Core.Catalysts.CatalystPreClass"/> name (IP-T487); null on a row no build has typed yet.</summary>
    [MaxLength(24)] public string? CatalystType { get; set; }

    /// <summary>The item is a letter of intent, MOU, term sheet or otherwise non-binding (IP-T487).</summary>
    public bool NonBindingLoi { get; set; }

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

    /// <summary>
    /// IP-T602: the earliest instant any intake source showed this story to us (min of its sightings' SeenUtc); null on
    /// rows from before sightings existed (read <see cref="ReceivedUtc"/>).
    /// </summary>
    public DateTime? FirstSeenUtc { get; set; }

    /// <summary>IP-T602: the source whose sighting is <see cref="FirstSeenUtc"/> ("alpaca-news", "prnewswire", ...).</summary>
    [MaxLength(32)] public string? FirstSeenSource { get; set; }

    /// <summary>
    /// IP-T602: set when this row is a later copy of a story another row already holds (same symbol, similar headline,
    /// inside the window): that row's id. Alpaca news and EDGAR keep their own rows; the press-release wires merge into the story.
    /// </summary>
    public Guid? StoryId { get; set; }
}

/// <summary>
/// One source seeing one story (IP-T602): the "who knew first" measurement. Every intake source that carries the story
/// gets a row on the story's first event, with the time the source stamped it and the time we saw it.
/// </summary>
public sealed class CatalystSighting
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The story's event (the row other copies point at with <see cref="CatalystEvent.StoryId"/>).</summary>
    public Guid EventId { get; set; }

    [MaxLength(32)] public string Source { get; set; } = "";
    [MaxLength(64)] public string ExternalId { get; set; } = "";

    /// <summary>When the source says it published the item.</summary>
    public DateTime PublishedUtc { get; set; }

    /// <summary>When this process first saw the item from this source.</summary>
    public DateTime SeenUtc { get; set; }

    [MaxLength(300)] public string Headline { get; set; } = "";
    [MaxLength(1000)] public string? Url { get; set; }

    /// <summary>Headline similarity to the story's event (1 = the event's own item).</summary>
    public double Similarity { get; set; } = 1;

    public bool Backfilled { get; set; }
}

/// <summary>
/// One social-attention reading (IP-T602): StockTwits trending rank, StockTwits per-symbol message count, or a Reddit
/// subreddit's mention count, over a window. A time series, not a catalyst; <see cref="FirstSeenUtc"/> is the first
/// reading for this source/channel/symbol in the ET day.
/// </summary>
public sealed class SocialSignal
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>"stocktwits-trending" | "stocktwits-messages" | "reddit".</summary>
    [MaxLength(32)] public string Source { get; set; } = "";

    /// <summary>The subreddit for Reddit; null otherwise.</summary>
    [MaxLength(64)] public string? Channel { get; set; }

    [MaxLength(16)] public string Symbol { get; set; } = "";

    public DateTime ObservedUtc { get; set; }
    public int WindowMinutes { get; set; }

    /// <summary>Messages or mentions in the window (1 for a trending-list presence).</summary>
    public int Count { get; set; }

    /// <summary>True when <see cref="Count"/> hit the page the source returns (the real count is at least this).</summary>
    public bool Capped { get; set; }

    /// <summary>Trending rank (1 = top), when the source ranks.</summary>
    public int? Rank { get; set; }

    /// <summary>The source's own score (StockTwits trending_score), when it gives one.</summary>
    public double? Score { get; set; }

    public DateTime FirstSeenUtc { get; set; }
}

/// <summary>One (event, symbol) pair so every symbol an item names is queryable, not only the primary.</summary>
public sealed class CatalystEventSymbol
{
    public Guid EventId { get; set; }
    [MaxLength(16)] public string Symbol { get; set; } = "";
}
