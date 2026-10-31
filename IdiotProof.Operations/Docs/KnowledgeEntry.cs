namespace IdiotProof.Operations.Docs;

/// <summary>What a <see cref="KnowledgeEntry"/> describes; <c>docs search</c> filters and labels by it.</summary>
public enum KnowledgeKind
{
    /// <summary>A numbered BIBLE section (<c>IP-§4</c>, <c>IP-§4.4</c>).</summary>
    Section,

    /// <summary>A BIBLE law (<c>IP-LAW-3</c>).</summary>
    Law,

    /// <summary>Any other markdown heading (README, AMENDMENTS, the digest, protocol docs).</summary>
    Doc,

    /// <summary>A user story (<c>IP-US-K1</c>).</summary>
    Story,

    /// <summary>A ticket (<c>IP-T259</c>), from the USER_STORIES ticket table or the tickets CLI.</summary>
    Ticket,

    /// <summary>A type or member with its XML doc comment and source location.</summary>
    Symbol,

    /// <summary>An entry of a static catalog (gapper profile, index event, Legion voter, setting).</summary>
    Catalog,
}

/// <summary>
/// One indexed piece of knowledge: a heading's section, a law, a story, a ticket, a documented
/// symbol or a catalog entry. <see cref="Id"/> is the stable handle (<c>IP-LAW-3</c>,
/// <c>IP-US-K1</c>, <c>T:IdiotProof.Core.Trading.TransactionGateway</c>); <see cref="Source"/> is a
/// repo-relative path with forward slashes and <see cref="Line"/> its 1-based line (0 = unknown).
/// </summary>
public sealed record KnowledgeEntry(
    KnowledgeKind Kind,
    string Id,
    string Title,
    string Text,
    string Source,
    int Line)
{
    /// <summary>Status where the source has one: a story's done/partial/planned, a ticket's open/closed.</summary>
    public string? Status { get; init; }

    /// <summary>The enclosing entry's id (a subsection's section, a story's epic heading), when known.</summary>
    public string? Parent { get; init; }

    /// <summary><c>path:line</c> for people and editors (just the path when the line is unknown).</summary>
    public string Location => Line > 0 ? $"{Source}:{Line}" : Source;
}
