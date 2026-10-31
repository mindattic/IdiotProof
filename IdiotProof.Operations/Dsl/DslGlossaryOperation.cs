using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Dsl;

public sealed record DslGlossaryArgs
{
    [Argument(0, Name = "verb", Description = "A verb name or alias (IsAboveVwap, StopLoss, RequireAdxAbove, ...); omit to list every entry.", Required = false)]
    public string? Verb { get; init; }
}

/// <summary>
/// How each IdiotScript verb is explained to a person, from <see cref="DslGlossary"/>: the same
/// title and one-line description the strategy cards and the Learning Center show. An alias
/// (<c>RequireAdxAbove</c>, <c>IsBreakingOut</c>) resolves to its canonical verb.
/// </summary>
[Operation("dsl glossary", Summary = "Plain-English meaning of IdiotScript verbs: every entry, or one verb (aliases resolve).", Risk = OperationRisk.Read)]
[Example("idiotproof dsl glossary")]
[Example("idiotproof dsl glossary IsAboveVwap")]
public sealed class DslGlossaryOperation : IOperation<DslGlossaryArgs>
{
    public Task<OperationResult> ExecuteAsync(DslGlossaryArgs args, OperationContext ctx)
    {
        var verb = DslVerbsOperation.NameOf(args.Verb ?? "");
        if (verb.Length == 0)
        {
            var all = DslGlossary.All.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Verb").Column("Title").Column("Meaning");
            foreach (var e in all) table.Row(e.Key, e.Title, e.Description);
            return Task.FromResult(OperationResult.Ok(new { entries = all.Select(e => new { e.Key, e.Title, e.Description }) },
                table.Render() + $"\n{all.Count} verbs. `idiotproof dsl verbs` lists every signature by phase."));
        }

        var entry = DslGlossary.Find(verb)
            ?? DslGlossary.All.FirstOrDefault(e => e.Key.Equals(verb, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            var near = DslGlossary.All
                .Select(e => (e.Key, Distance: OperationNames.Distance(verb.ToLowerInvariant(), e.Key.ToLowerInvariant())))
                .Where(x => x.Distance <= 3 || x.Key.Contains(verb, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Distance).Take(3).Select(x => x.Key).ToList();
            return Task.FromResult(OperationResult.Usage($"No DSL glossary entry for '{verb}'." +
                (near.Count > 0 ? $" Did you mean {string.Join(", ", near)}?" : " `idiotproof dsl glossary` lists every verb.")));
        }

        var aliases = DslGlossary.AliasesOf(entry.Key);
        var block = new KeyValueBlock()
            .Add("Verb", entry.Key)
            .Add("Title", entry.Title)
            .Add("Meaning", entry.Description)
            .Add("Also spelled", aliases.Count == 0 ? null : string.Join(", ", aliases), "-");
        if (!entry.Key.Equals(verb, StringComparison.Ordinal))
            block.Add("You asked for", verb);
        return Task.FromResult(OperationResult.Ok(new { entry.Key, entry.Title, entry.Description, aliases }, block.Render()));
    }
}
