using System.Text;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Docs;

public sealed record DocsLawArgs
{
    [Argument(0, Name = "n", Description = "The law: 3, LAW-3 or IP-LAW-3. Leave out to list every law.", Required = false)]
    public string? Law { get; init; }
}

public sealed record DocsSectionArgs
{
    [Argument(0, Name = "section", Description = "The BIBLE section: 4, §4, 4.4 or IP-§4. Leave out to list the sections.", Required = false)]
    public string? Section { get; init; }
}

public sealed record DocsStoryArgs
{
    [Argument(0, Name = "id", Description = "The story: K1, US-K1 or IP-US-K1. Leave out and pass --epic or --status to list stories.", Required = false)]
    public string? Id { get; init; }

    [Option("epic", Description = "List the stories of one epic by letter (K, W, ...).")]
    public string? Epic { get; init; }

    [Option("status", Description = "List only stories with this status: done, partial or planned.")]
    public string? Status { get; init; }
}

/// <summary>Shared rendering for the <c>docs</c> lookups.</summary>
internal static class DocsRender
{
    private const char NL = (char)10;

    /// <summary>A heading line, the location, then the text, as a person reads it.</summary>
    public static string Entry(KnowledgeEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Id).Append(" — ").Append(e.Title).Append(NL);
        sb.Append("Where: ").Append(e.Location);
        if (e.Status is { } s) sb.Append("   Status: ").Append(s);
        if (e.Parent is { } p) sb.Append("   In: ").Append(p);
        sb.Append(NL).Append(NL);
        sb.Append(e.Text.Length == 0 ? "(no text)" : e.Text.TrimEnd());
        return sb.ToString();
    }

    public static object Data(KnowledgeEntry e) => new
    {
        kind = e.Kind.ToString().ToLowerInvariant(),
        id = e.Id,
        title = e.Title,
        status = e.Status,
        parent = e.Parent,
        source = e.Source,
        line = e.Line,
        location = e.Location,
        text = e.Text,
    };

    /// <summary>A list of entries as an Id / Title / Where table.</summary>
    public static string Table(IEnumerable<KnowledgeEntry> entries, int? width, bool status = false)
    {
        var table = new TextTable { MaxWidth = width }.Column("Id").Column("Title");
        if (status) table.Column("Status");
        table.Column("Where");
        foreach (var e in entries)
        {
            if (status) table.Row(e.Id, e.Title, e.Status ?? "", e.Location);
            else table.Row(e.Id, e.Title, e.Location);
        }
        return table.Render();
    }

    public static OperationException NotFound(string what, string input, KnowledgeIndex index, string hint)
    {
        var near = KnowledgeSearch.Search(index, input, 3).Select(h => h.Entry.Id).ToList();
        return OperationException.Failed($"No {what} '{input}'. {hint}" + (near.Count > 0 ? $" Closest: {string.Join(", ", near)}." : ""));
    }
}

/// <summary><c>docs law</c> (IP-T267): one IP-LAW in full, or the list of laws.</summary>
[Operation("docs law", Summary = "Show one BIBLE law (IP-LAW-n) in full, or list every law.", Risk = OperationRisk.Read)]
[Example("idiotproof docs law 3")]
[Example("idiotproof docs law", Description = "Every law with its title and line.")]
public sealed class DocsLawOperation : IOperation<DocsLawArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsLawArgs args, OperationContext ctx)
    {
        var index = await KnowledgeService.From(ctx).GetAsync(ct: ctx.CancellationToken);
        if (string.IsNullOrWhiteSpace(args.Law))
        {
            var laws = index.OfKind(KnowledgeKind.Law).OrderBy(l => LawNumber(l.Id)).ToList();
            return OperationResult.Ok(laws.Select(DocsRender.Data), $"{laws.Count} laws (docs/BIBLE.md §5)\n\n" + DocsRender.Table(laws, ctx.Width));
        }
        var id = KnowledgeIndex.LawId(args.Law) ?? throw OperationException.Usage($"'{args.Law}' is not a law number. Try `docs law 3`.");
        var law = index.Get(id) is { Kind: KnowledgeKind.Law } found ? found
            : throw DocsRender.NotFound("law", args.Law, index, "Run `docs law` for the list.");
        return OperationResult.Ok(DocsRender.Data(law), DocsRender.Entry(law));
    }

    private static int LawNumber(string id) => int.TryParse(id["IP-LAW-".Length..], out var n) ? n : int.MaxValue;
}

/// <summary><c>docs section</c> (IP-T267): one BIBLE section (subsections included), or the table of contents.</summary>
[Operation("docs section", Summary = "Show one BIBLE section (IP-§n, subsections included), or the table of contents.", Risk = OperationRisk.Read)]
[Example("idiotproof docs section 4")]
[Example("idiotproof docs section 4.4", Description = "A numbered subsection.")]
[Example("idiotproof docs section", Description = "The BIBLE's table of contents.")]
public sealed class DocsSectionOperation : IOperation<DocsSectionArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsSectionArgs args, OperationContext ctx)
    {
        var index = await KnowledgeService.From(ctx).GetAsync(ct: ctx.CancellationToken);
        if (string.IsNullOrWhiteSpace(args.Section))
        {
            var sections = index.OfKind(KnowledgeKind.Section).OrderBy(s => s.Line).ToList();
            var toc = new StringBuilder($"docs/BIBLE.md — {sections.Count} sections\n\n");
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Section").Column("Title").Column("Line", Align.Right);
            foreach (var s in sections)
                table.Row(s.Parent is null ? s.Id : "  " + s.Id, s.Title, s.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return OperationResult.Ok(sections.Select(DocsRender.Data), toc.Append(table.Render()).ToString());
        }
        var id = KnowledgeIndex.SectionId(args.Section) ?? throw OperationException.Usage($"'{args.Section}' is not a section number. Try `docs section 4`.");
        var section = index.Get(id) is { Kind: KnowledgeKind.Section } found ? found
            : throw DocsRender.NotFound("section", args.Section, index, "Run `docs section` for the table of contents.");
        return OperationResult.Ok(DocsRender.Data(section), DocsRender.Entry(section));
    }
}

/// <summary><c>docs story</c> (IP-T267): one user story with its status and proof, or a filtered list.</summary>
[Operation("docs story", Summary = "Show one user story (IP-US-*) with its status and proving tests, or list an epic's stories.", Risk = OperationRisk.Read)]
[Example("idiotproof docs story K1")]
[Example("idiotproof docs story --epic W", Description = "Every Epic W story with its status.")]
[Example("idiotproof docs story --status planned")]
public sealed class DocsStoryOperation : IOperation<DocsStoryArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsStoryArgs args, OperationContext ctx)
    {
        var index = await KnowledgeService.From(ctx).GetAsync(ct: ctx.CancellationToken);
        if (!string.IsNullOrWhiteSpace(args.Id))
        {
            var id = KnowledgeIndex.StoryId(args.Id) ?? throw OperationException.Usage($"'{args.Id}' is not a story id. Try `docs story K1`.");
            var story = index.Get(id) is { Kind: KnowledgeKind.Story } found ? found
                : throw DocsRender.NotFound("story", args.Id, index, "Run `docs story --epic <letter>` to list an epic.");
            return OperationResult.Ok(DocsRender.Data(story), DocsRender.Entry(story));
        }

        if (args.Epic is null && args.Status is null)
            return OperationResult.Usage("Name a story (`docs story K1`) or filter with --epic and/or --status.");

        var epic = args.Epic?.Trim().ToUpperInvariant();
        var stories = index.OfKind(KnowledgeKind.Story)
            .Where(s => epic is null || s.Id.StartsWith($"IP-US-{epic}", StringComparison.OrdinalIgnoreCase) && s.Id.Length > 6 + epic.Length && char.IsDigit(s.Id[6 + epic.Length]))
            .Where(s => args.Status is null || string.Equals(s.Status, args.Status.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var counts = string.Join(", ", stories.GroupBy(s => s.Status ?? "unknown").OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
        var heading = $"{stories.Count} stor{(stories.Count == 1 ? "y" : "ies")}"
                      + (epic is null ? "" : $" in Epic {epic}") + (args.Status is null ? "" : $" ({args.Status})")
                      + (stories.Count > 0 && args.Status is null ? $": {counts}" : "");
        return OperationResult.Ok(stories.Select(DocsRender.Data), heading + (stories.Count == 0 ? "" : "\n\n" + DocsRender.Table(stories, ctx.Width, status: true)));
    }
}
