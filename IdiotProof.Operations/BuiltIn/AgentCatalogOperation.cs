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
/// </summary>
[Operation("agent catalog", Summary = "List every operation with its parameters, risk and input schema.", Risk = OperationRisk.Read)]
[Example("idiotproof agent catalog")]
[Example("idiotproof agent catalog --json", Description = "The full machine-readable catalog (what /api/catalog serves).")]
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
        return Task.FromResult(OperationResult.Ok(catalog.ToJsonNode(), human.ToString()));
    }
}
