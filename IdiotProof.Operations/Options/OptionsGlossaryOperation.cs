using IdiotProof.Operations.Rendering;
using IdiotProof.Shared.Options;

namespace IdiotProof.Operations.Options;

public sealed record OptionsGlossaryArgs
{
    [Argument(0, Name = "term", Description = "A glossary key or title word (hype, breakeven, iv, sell-to-close, ...); omit to list every term.", Required = false)]
    public string? Term { get; init; }
}

/// <summary>
/// The Options section's plain-English jargon, from the same <see cref="OptionsGlossary"/> the web
/// page's hover hints read, so a term is never explained two different ways. No term lists them
/// all with their one-line hint; a term prints the full explanation.
/// </summary>
[Operation("options glossary", Summary = "Plain-English options jargon (hype, breakeven, IV, sell to close, ...): every term, or one in full.", Risk = OperationRisk.Read)]
[Example("idiotproof options glossary")]
[Example("idiotproof options glossary hype", Description = "The full explanation of one term.")]
public sealed class OptionsGlossaryOperation : IOperation<OptionsGlossaryArgs>
{
    public Task<OperationResult> ExecuteAsync(OptionsGlossaryArgs args, OperationContext ctx)
    {
        var all = OptionsGlossary.All.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        var term = args.Term?.Trim() ?? "";
        if (term.Length == 0)
        {
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Term").Column("Title").Column("In one line");
            foreach (var e in all) table.Row(e.Key, e.Title, e.Short);
            var human = table.Render() + $"\n{all.Count} terms. `idiotproof options glossary <term>` explains one in full.";
            return Task.FromResult(OperationResult.Ok(new { terms = all }, human));
        }

        var entry = Find(all, term);
        if (entry is null)
        {
            var near = all
                .Select(e => (e.Key, Distance: Math.Min(OperationNames.Distance(term.ToLowerInvariant(), e.Key), OperationNames.Distance(term.ToLowerInvariant(), e.Title.ToLowerInvariant()))))
                .Where(x => x.Distance <= 3 || x.Key.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Distance).Take(3).Select(x => x.Key).ToList();
            return Task.FromResult(OperationResult.Usage($"No options glossary entry for '{term}'." +
                (near.Count > 0 ? $" Did you mean {string.Join(", ", near)}?" : " `idiotproof options glossary` lists every term.")));
        }

        var block = new KeyValueBlock()
            .Add("Term", entry.Key)
            .Add("Title", entry.Title)
            .Add("In one line", entry.Short);
        return Task.FromResult(OperationResult.Ok(new { term = entry }, block.Render() + "\n\n" + entry.Long));
    }

    /// <summary>Exact key first, then a key with spaces for dashes, then an exact or leading title word.</summary>
    internal static JargonEntry? Find(IReadOnlyList<JargonEntry> all, string term)
    {
        var dashed = term.Replace(' ', '-');
        return all.FirstOrDefault(e => e.Key.Equals(term, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => e.Key.Equals(dashed, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => e.Title.Equals(term, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(e => e.Title.StartsWith(term + " ", StringComparison.OrdinalIgnoreCase));
    }
}
