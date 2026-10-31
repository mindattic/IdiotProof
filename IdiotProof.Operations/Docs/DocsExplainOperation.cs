using System.Text;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Docs;

public sealed record DocsExplainArgs
{
    [Argument(0, Name = "symbol", Description = "A type or member: TransactionGateway, TransactionGateway.SubmitAsync, a full name, or an XML doc id (T:..., M:...).")]
    public string Symbol { get; init; } = "";

    [Option("callers", Description = "Most lines that mention it to list (default 15; 0 for none).")]
    public int Callers { get; init; } = 15;
}

/// <summary>
/// <c>docs explain</c> (IP-T268): what a type or member is for (its XML doc comment), where it is
/// declared (<c>path:line</c>), what a type's documented members are, and which lines mention it.
/// </summary>
[Operation("docs explain", Summary = "Explain a type or member: its doc comment, where it is declared (path:line), its members and its callers.", Risk = OperationRisk.Read,
    Description = "Callers are found by text (every line naming it as a whole word, comments and the declaration skipped), so overloads and same-named members elsewhere also show.")]
[Example("idiotproof docs explain TransactionGateway")]
[Example("idiotproof docs explain TransactionGateway.SubmitAsync --callers 5")]
[Example("idiotproof docs explain T:IdiotProof.Core.Trading.OrderIntent --json")]
public sealed class DocsExplainOperation : IOperation<DocsExplainArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsExplainArgs args, OperationContext ctx)
    {
        var input = args.Symbol.Trim();
        if (input.Length == 0) return OperationResult.Usage("Name a type or member, e.g. `idiotproof docs explain TransactionGateway`.");
        var index = await KnowledgeService.From(ctx).GetAsync(ct: ctx.CancellationToken);

        var matches = Resolve(index, input);
        if (matches.Count == 0)
            throw DocsRender.NotFound("type or member", input, index, "Try the short type name, or `docs search` with words from it.");
        if (matches.Count > 1)
        {
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Id").Column("Where");
            foreach (var m in matches.Take(25)) table.Row(m.Id, m.Location);
            return OperationResult.Ok(new { input, ambiguous = matches.Select(DocsRender.Data) },
                $"'{input}' names {matches.Count} symbols; pass a full name or one of these ids:\n\n{table.Render()}");
        }

        var symbol = matches[0];
        var isType = symbol.Id.StartsWith("T:", StringComparison.Ordinal);
        var members = isType
            ? index.OfKind(KnowledgeKind.Symbol).Where(e => e.Parent == symbol.Id).OrderBy(e => e.Line == 0 ? int.MaxValue : e.Line).ToList()
            : [];
        var simpleName = symbol.Title.Contains('.') ? symbol.Title[(symbol.Title.LastIndexOf('.') + 1)..] : symbol.Title;
        if (simpleName == "ctor") simpleName = symbol.Title[..symbol.Title.IndexOf('.')];
        var declared = symbol.Line > 0 ? new SourceLocation(symbol.Source, symbol.Line) : null;
        var callers = args.Callers > 0 && index.Sources is { } sources
            ? sources.References(simpleName, args.Callers, declared)
            : [];

        var sb = new StringBuilder();
        sb.Append(symbol.Title).Append("  (").Append(symbol.Id).Append(")\n");
        sb.Append("Declared: ").Append(symbol.Line > 0 ? symbol.Location : "(source not found in the checkout)").Append("\n\n");
        sb.Append(symbol.Text.Length == 0 ? "(no doc comment)" : symbol.Text.TrimEnd()).Append('\n');
        if (members.Count > 0)
        {
            sb.Append("\nMembers:\n");
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Member").Column("Summary").Column("Line", Align.Right);
            foreach (var m in members)
                table.Row(m.Title[(m.Title.IndexOf('.') + 1)..], FirstLine(m.Text), m.Line > 0 ? m.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            sb.Append(table.Render()).Append('\n');
        }
        if (callers.Count > 0)
        {
            sb.Append($"\nMentioned at ({callers.Count}{(callers.Count >= args.Callers ? "+" : "")}):\n");
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Where").Column("Line text");
            foreach (var c in callers) table.Row(c.ToString(), c.Text);
            sb.Append(table.Render()).Append('\n');
        }

        var data = new
        {
            symbol = DocsRender.Data(symbol),
            members = members.Select(m => new { id = m.Id, name = m.Title, summary = FirstLine(m.Text), location = m.Location }),
            callers = callers.Select(c => new { location = c.ToString(), path = c.Path, line = c.Line, text = c.Text }),
        };
        return OperationResult.Ok(data, sb.ToString().TrimEnd());
    }

    /// <summary>Symbols named by <paramref name="input"/>: exact doc id, full name, <c>Type.Member</c>, or a bare type/member name.</summary>
    internal static IReadOnlyList<KnowledgeEntry> Resolve(KnowledgeIndex index, string input)
    {
        if (input.Length > 2 && input[1] == ':' && index.Get(input) is { Kind: KnowledgeKind.Symbol } exact) return [exact];
        var symbols = index.OfKind(KnowledgeKind.Symbol).ToList();

        var byFullName = symbols.Where(s => string.Equals(XmlDocs.StripParameters(s.Id[2..]), input, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byFullName.Count > 0) return Prefer(byFullName);

        var byTitle = symbols.Where(s => string.Equals(s.Title, input, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byTitle.Count > 0) return Prefer(byTitle);

        return Prefer([.. symbols.Where(s => s.Title.EndsWith("." + input, StringComparison.OrdinalIgnoreCase))]);
    }

    /// <summary>Overloads collapse to the first; a type wins over same-named members.</summary>
    private static IReadOnlyList<KnowledgeEntry> Prefer(List<KnowledgeEntry> found)
    {
        var types = found.Where(f => f.Id.StartsWith("T:", StringComparison.Ordinal)).ToList();
        var pool = types.Count > 0 ? types : found;
        return [.. pool.GroupBy(f => XmlDocs.StripParameters(f.Id)).Select(g => g.First())];
    }

    private static string FirstLine(string text)
    {
        var t = text.Trim();
        var nl = t.IndexOf('\n');
        return nl < 0 ? t : t[..nl];
    }
}
