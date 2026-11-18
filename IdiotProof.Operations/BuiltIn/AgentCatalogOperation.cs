using System.Text;

namespace IdiotProof.Operations.BuiltIn;

public sealed record AgentCatalogArgs
{
    [Option("operation", Short = 'o', Description = "Only this operation, by name (\"strategy list\") or tool name (strategy_list).")]
    public string? Operation { get; init; }
}

/// <summary>
/// The catalog itself as an operation, so an agent can discover every tool, its parameters and
/// its risk through the same channel it calls them. Human mode prints a name/risk/summary list,
/// or one operation's help.
/// <para>
/// The JSON is self-describing (IP-T526), every part reflected from the code that enforces it
/// (IP-LAW-4). Each parameter carries its type, <c>value_kind</c>, accepted <c>format</c>, whether
/// it is required, its default, its <c>enum_values</c> and an <c>example</c>. Beside the
/// operations: <c>value_formats</c> (how each value kind may be written, what is corrected and
/// what never is, from the binder's own tables), <c>errors</c> (every status, the schema of a
/// usage error with its corrected call, and how to apply it) and <c>dsl</c> (every IdiotScript
/// diagnostic code with what its fix does, the repair payload's schema, and the JSON Schema of
/// the canonical strategy JSON).
/// </para>
/// </summary>
[Operation("agent catalog", Summary = "List every operation with its parameters (type, format, default, allowed values, example), risk and input schema, plus the value formats, the error payload and the DSL diagnostics and schema.", Risk = OperationRisk.Read)]
[Example("idiotproof agent catalog")]
[Example("idiotproof agent catalog --json", Description = "The full machine-readable catalog (what /api/catalog serves): operations, value_formats, errors, dsl.")]
[Example("idiotproof agent catalog --operation \"hub ping\"", Description = "One operation's help.")]
public sealed class AgentCatalogOperation(OperationCatalog catalog) : IOperation<AgentCatalogArgs>
{
    public Task<OperationResult> ExecuteAsync(AgentCatalogArgs args, OperationContext ctx)
    {
        if (args.Operation is { } name)
        {
            var one = catalog.Find(name)
                ?? throw OperationException.Usage(catalog.Suggest(name) is { } s
                    ? $"Unknown operation '{name}'. Did you mean '{s}'?"
                    : $"Unknown operation '{name}'.");
            return Task.FromResult(OperationResult.Ok(one.ToJson(), one.Help));
        }

        var width = catalog.Operations.Max(o => o.Name.Length);
        var human = new StringBuilder();
        foreach (var o in catalog.Operations)
            human.Append(o.Name.PadRight(width)).Append("  ").Append(o.Risk.ToString().ToLowerInvariant().PadRight(11)).AppendLine(o.Summary);
        human.AppendLine().Append("--json adds, for every parameter, its format and an example, and the sections value_formats, errors (the usage-error payload and how to apply its corrected call) and dsl (diagnostic codes, the repair payload, the strategy JSON schema).");
        return Task.FromResult(OperationResult.Ok(catalog.ToJsonNode(), human.ToString()));
    }
}
