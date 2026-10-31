using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Docs;

public sealed record DocsSearchArgs
{
    [Argument(0, Name = "text", Description = "What to look for: words, a phrase, or an id like IP-LAW-3, IP-US-K1, IP-T259, §4, TransactionGateway.")]
    public string[] Text { get; init; } = [];

    [Option("kind", Description = "Only one kind: section, law, doc, story, ticket, symbol or catalog.")]
    public KnowledgeKind? Kind { get; init; }

    [Option("limit", Description = "Most results to show (default 15).")]
    public int Limit { get; init; } = 15;

    [Option("refresh", Description = "Re-read the docs, code and tickets first (the index otherwise refreshes every 30 minutes).")]
    public bool Refresh { get; init; }
}

/// <summary>
/// <c>docs search</c> (IP-T266): one ranked search across the BIBLE, the other docs, every user
/// story and ticket, every documented type and member, and the static catalogs.
/// </summary>
[Operation("docs search", Summary = "Search everything IdiotProof knows about itself: BIBLE, docs, stories, tickets, code docs and catalogs.", Risk = OperationRisk.Read,
    Description = "Results are ranked: an exact id first, then entries holding every word (title and id hits weigh most). Follow up with `docs law`, `docs section`, `docs story`, `docs ticket` or `docs explain`.")]
[Example("idiotproof docs search transaction gateway")]
[Example("idiotproof docs search \"peak giveback\" --kind story")]
[Example("idiotproof docs search IP-LAW-9 --json", Description = "Machine-readable hits with path:line and a snippet.")]
public sealed class DocsSearchOperation : IOperation<DocsSearchArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsSearchArgs args, OperationContext ctx)
    {
        var query = string.Join(' ', args.Text).Trim();
        if (query.Length == 0) return OperationResult.Usage("Say what to search for, e.g. `idiotproof docs search risk guardian`.");

        var index = await KnowledgeService.From(ctx).GetAsync(args.Refresh, ctx.CancellationToken);
        var hits = KnowledgeSearch.Search(index, query, args.Limit, args.Kind);
        var data = new
        {
            query,
            kind = args.Kind?.ToString().ToLowerInvariant(),
            indexed = index.Count,
            builtEt = ctx.Et(index.BuiltUtc),
            hits = hits.Select(h => new
            {
                kind = h.Entry.Kind.ToString().ToLowerInvariant(),
                id = h.Entry.Id,
                title = h.Entry.Title,
                status = h.Entry.Status,
                location = h.Entry.Location,
                score = h.Score,
                snippet = h.Snippet,
            }),
        };
        if (hits.Count == 0)
            return OperationResult.Ok(data, $"Nothing matches \"{query}\" in {index.Count} indexed entries. Try fewer or different words.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("Kind")
            .Column("Id", maxWidth: 48)
            .Column("Title")
            .Column("Where", maxWidth: 52);
        foreach (var h in hits)
            table.Row(h.Entry.Kind.ToString().ToLowerInvariant(), h.Entry.Id, h.Entry.Status is { } s ? $"{h.Entry.Title} [{s}]" : h.Entry.Title, h.Entry.Location);

        var human = $"{hits.Count} result{(hits.Count == 1 ? "" : "s")} for \"{query}\" ({index.Count} entries indexed {ctx.Et(index.BuiltUtc)})\n\n"
                    + table.Render()
                    + $"\n\nTop hit: {hits[0].Snippet}";
        return OperationResult.Ok(data, human);
    }
}
