namespace IdiotProof.Operations;

/// <summary>
/// How much harm an operation can do. Front doors gate on it: Read runs freely, Write changes
/// IdiotProof state, Money can place or cancel orders, Destructive deletes data. The MCP server
/// and the CLI surface it so an agent or a person knows what they are about to run.
/// </summary>
public enum OperationRisk
{
    Read,
    Write,
    Money,
    Destructive,
}

/// <summary>
/// Marks a class implementing <see cref="IOperation{TArgs}"/> as an operation. The name is
/// <c>"noun verb"</c> in lower kebab-case words (<c>"strategy list"</c>, <c>"order cancel-all"</c>);
/// the CLI command, the MCP tool name (<c>strategy_list</c>) and the docs entry all come from it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OperationAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>One line, shown in <c>--help</c>, the catalog and as the MCP tool description. Required.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Optional longer prose appended after the summary in help and docs.</summary>
    public string? Description { get; init; }

    public OperationRisk Risk { get; init; } = OperationRisk.Read;
}

/// <summary>
/// A worked command line for docs and help (<c>"idiotproof strategy list --active"</c>). The
/// catalog completeness test binds every example, so an example that drifts from the args record
/// fails the build's tests instead of misleading a reader.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ExampleAttribute(string commandLine) : Attribute
{
    public string CommandLine { get; } = commandLine;

    /// <summary>Optional one-line explanation printed under the example.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// A named parameter on an args record: <c>--name value</c> on the CLI, <c>name</c> (snake_case)
/// in the MCP/Hub JSON. Names are lower kebab-case. A bool property is a flag (<c>--active</c>,
/// <c>--active=false</c>, <c>--no-active</c>). On a positional record put it on the property:
/// <c>[property: Option("limit", Description = "...")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class OptionAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>Required — the catalog rejects an undocumented option.</summary>
    public string Description { get; init; } = "";

    public bool Required { get; init; }

    /// <summary>Optional one-letter alias (<c>-a</c>); <c>'\0'</c> means none.</summary>
    public char Short { get; init; }
}

/// <summary>
/// A positional parameter on an args record (<c>idiotproof strategy show 42</c>). Positions run
/// 0, 1, 2… with no gaps; required ones come first; a <c>string[]</c> argument may only be last
/// and takes every remaining positional. In JSON it is passed by name like an option.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ArgumentAttribute(int position) : Attribute
{
    public int Position { get; } = position;

    /// <summary>Name in usage lines and JSON; defaults to the property name in kebab-case.</summary>
    public string? Name { get; init; }

    /// <summary>Required — the catalog rejects an undocumented argument.</summary>
    public string Description { get; init; } = "";

    /// <summary>Positional arguments are required unless said otherwise.</summary>
    public bool Required { get; init; } = true;
}
