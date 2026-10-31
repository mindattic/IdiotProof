using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Dsl;

public sealed record DslVerbsArgs
{
    [Option("phase", Description = "Only one phase: setup, filters, entry, order, risk, exit, or conditions (the static If/ElseIf catalog).")]
    public string? Phase { get; init; }
}

/// <summary>One IdiotScript verb as <c>dsl verbs</c> reports it.</summary>
public sealed record DslVerb(string Phase, string Name, string Signature, string? Title, string? Description);

/// <summary>
/// The IdiotScript verb catalog, reflected off the real <c>StrategyBuilder</c> and <c>Conditions</c>
/// types (IP-LAW-4) by the same <see cref="StrategyScriptGenerator"/> code that writes the LLM
/// prompt and the Learning Center, so the list can never drift from what actually compiles. Each
/// verb carries its <see cref="DslGlossary"/> title and description when it has one.
/// </summary>
[Operation("dsl verbs", Summary = "The IdiotScript verb catalog by phase, reflected from the code that compiles it (IP-LAW-4).", Risk = OperationRisk.Read)]
[Example("idiotproof dsl verbs")]
[Example("idiotproof dsl verbs --phase risk")]
[Example("idiotproof dsl verbs --phase conditions", Description = "The static Conditions catalog used inside .If/.ElseIf.")]
public sealed class DslVerbsOperation : IOperation<DslVerbsArgs>
{
    public const string ConditionsPhase = "Conditions";

    public Task<OperationResult> ExecuteAsync(DslVerbsArgs args, OperationContext ctx)
    {
        var groups = StrategyScriptGenerator.GetVerbsByPhase()
            .Select(g => (Phase: g.Phase, Role: g.Role, Verbs: g.Verbs))
            .Append((Phase: ConditionsPhase, Role: "Branch expressions for .If/.ElseIf", Verbs: StrategyScriptGenerator.GetConditionCatalog()))
            .ToList();

        if (!string.IsNullOrWhiteSpace(args.Phase))
        {
            var wanted = args.Phase.Trim();
            groups = groups.Where(g => g.Phase.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (groups.Count == 0)
                return Task.FromResult(OperationResult.Usage(
                    $"No phase '{wanted}'. Phases: setup, filters, entry, order, risk, exit, conditions."));
        }

        var verbs = groups.SelectMany(g => g.Verbs.Select(v =>
        {
            var name = NameOf(v);
            var entry = DslGlossary.Find(name);
            return new DslVerb(g.Phase, name, v, entry?.Title, entry?.Description);
        })).ToList();

        var blocks = new List<string>();
        foreach (var g in groups)
        {
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Verb").Column("Meaning");
            foreach (var v in verbs.Where(v => v.Phase == g.Phase))
                table.Row(v.Signature, v.Description ?? v.Title ?? "");
            blocks.Add($"{g.Phase.ToUpperInvariant()} — {g.Role} ({g.Verbs.Count})\n{table.Render()}");
        }
        blocks.Add($"{verbs.Count} verbs, reflected from StrategyBuilder and Conditions. `idiotproof dsl glossary <verb>` explains one.");
        return Task.FromResult(OperationResult.Ok(new { phases = groups.Select(g => new { phase = g.Phase, role = g.Role, count = g.Verbs.Count }), verbs },
            string.Join("\n\n", blocks)));
    }

    /// <summary><c>StopLoss(double price)</c> → <c>StopLoss</c>.</summary>
    internal static string NameOf(string signature)
    {
        var i = signature.IndexOf('(');
        return (i < 0 ? signature : signature[..i]).Trim();
    }
}
