using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdiotProof.Operations;

/// <summary>Option (<c>--name</c>) or positional argument.</summary>
public enum ParameterKind
{
    Option,
    Argument,
}

/// <summary>The CLR shapes an args property may take (each also allowed as nullable).</summary>
public enum ParameterValueKind
{
    String,
    Boolean,
    Int32,
    Int64,
    Decimal,
    Double,
    Guid,
    Date,
    Time,
    Enum,
    StringArray,
}

/// <summary>An example command line from <see cref="ExampleAttribute"/>.</summary>
public sealed record OperationExample(string CommandLine, string? Description);

/// <summary>One option or argument of an operation, reflected from its args record.</summary>
public sealed class OperationParameter
{
    /// <summary>Kebab-case CLI name (<c>max-price</c>).</summary>
    public required string Name { get; init; }

    /// <summary>snake_case JSON / MCP property name (<c>max_price</c>).</summary>
    public string JsonName => OperationNames.ToSnake(Name);

    public required ParameterKind Kind { get; init; }

    /// <summary>Position for an argument; null for an option.</summary>
    public int? Position { get; init; }

    /// <summary>One-letter alias for an option, or null.</summary>
    public char? Short { get; init; }

    public required string Description { get; init; }

    public required bool Required { get; init; }

    public required ParameterValueKind ValueKind { get; init; }

    /// <summary>The property type with any <see cref="Nullable{T}"/> unwrapped.</summary>
    public required Type ValueType { get; init; }

    public required PropertyInfo Property { get; init; }

    /// <summary>The args record's default for this parameter (null when none).</summary>
    public object? DefaultValue { get; init; }

    /// <summary>For enums: the accepted kebab-case names, in declaration order.</summary>
    public IReadOnlyList<string> EnumValues { get; init; } = [];

    public bool IsFlag => ValueKind == ParameterValueKind.Boolean;

    public bool IsArray => ValueKind == ParameterValueKind.StringArray;

    /// <summary>Short type label for help and the catalog: <c>int</c>, <c>uuid</c>, <c>paper|live</c>, <c>string[]</c>.</summary>
    public string TypeLabel => ValueKind switch
    {
        ParameterValueKind.String => "string",
        ParameterValueKind.Boolean => "bool",
        ParameterValueKind.Int32 => "int",
        ParameterValueKind.Int64 => "long",
        ParameterValueKind.Decimal => "decimal",
        ParameterValueKind.Double => "number",
        ParameterValueKind.Guid => "uuid",
        ParameterValueKind.Date => "date",
        ParameterValueKind.Time => "time",
        ParameterValueKind.Enum => string.Join('|', EnumValues),
        ParameterValueKind.StringArray => "string[]",
        _ => "string",
    };

    /// <summary>The kebab-case wire name of an enum member (<c>ShortSale</c> → <c>short-sale</c>).</summary>
    public static string EnumWireName(object value) => OperationNames.ToKebab(value.ToString()!);

    /// <summary>This parameter's JSON Schema fragment (draft 2020-12).</summary>
    internal JsonObject ToSchema()
    {
        var schema = new JsonObject();
        switch (ValueKind)
        {
            case ParameterValueKind.String: schema["type"] = "string"; break;
            case ParameterValueKind.Boolean: schema["type"] = "boolean"; break;
            case ParameterValueKind.Int32 or ParameterValueKind.Int64: schema["type"] = "integer"; break;
            case ParameterValueKind.Decimal or ParameterValueKind.Double: schema["type"] = "number"; break;
            case ParameterValueKind.Guid: schema["type"] = "string"; schema["format"] = "uuid"; break;
            case ParameterValueKind.Date: schema["type"] = "string"; schema["format"] = "date"; break;
            case ParameterValueKind.Time:
                schema["type"] = "string";
                schema["pattern"] = "^[0-9]{1,2}:[0-9]{2}(:[0-9]{2})?$";
                break;
            case ParameterValueKind.Enum:
                schema["type"] = "string";
                schema["enum"] = new JsonArray([.. EnumValues.Select(v => (JsonNode)v)]);
                break;
            case ParameterValueKind.StringArray:
                schema["type"] = "array";
                schema["items"] = new JsonObject { ["type"] = "string" };
                break;
        }
        schema["description"] = ValueKind == ParameterValueKind.Time ? Description + " (ET, HH:mm)" : Description;
        if (DefaultJson() is { } def) schema["default"] = def;
        return schema;
    }

    /// <summary>The default as JSON, or null when there is no meaningful default to advertise.</summary>
    internal JsonNode? DefaultJson() => DefaultValue switch
    {
        null => null,
        string[] { Length: 0 } => null,
        Enum e => EnumWireName(e),
        TimeOnly t => t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        _ => JsonSerializer.SerializeToNode(DefaultValue, OperationJson.Options),
    };
}

/// <summary>
/// Everything the front doors need to know about one operation, reflected once from its class and
/// args record: identity, docs, risk, parameters, the MCP input schema, and how to build and run it.
/// </summary>
public sealed class OperationDescriptor
{
    /// <summary>The CLI program name every usage line and example starts with.</summary>
    public const string ProgramName = "idiotproof";

    private readonly JsonObject schema;
    private readonly Func<object, object, OperationContext, Task<OperationResult>> invoker;
    private readonly ConstructorInfo? argsConstructor;

    internal OperationDescriptor(
        OperationAttribute attribute,
        Type operationType,
        Type argsType,
        IReadOnlyList<OperationParameter> parameters,
        IReadOnlyList<OperationExample> examples,
        ConstructorInfo? argsConstructor)
    {
        Name = attribute.Name;
        var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Noun = words[0];
        Verb = string.Join(' ', words.Skip(1));
        ToolName = OperationNames.ToToolName(Name);
        Summary = attribute.Summary;
        Description = attribute.Description;
        Risk = attribute.Risk;
        OperationType = operationType;
        ArgsType = argsType;
        Parameters = parameters;
        Examples = examples;
        this.argsConstructor = argsConstructor;
        schema = BuildSchema();

        // One closed generic thunk per operation, so dispatch is a delegate call, not MethodInfo.Invoke.
        invoker = (Func<object, object, OperationContext, Task<OperationResult>>)Delegate.CreateDelegate(
            typeof(Func<object, object, OperationContext, Task<OperationResult>>),
            typeof(OperationDescriptor).GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(argsType));
    }

    /// <summary><c>strategy list</c>.</summary>
    public string Name { get; }

    /// <summary>The first word (<c>strategy</c>).</summary>
    public string Noun { get; }

    /// <summary>The remaining word(s) (<c>list</c>).</summary>
    public string Verb { get; }

    /// <summary>MCP tool name: <c>strategy_list</c>.</summary>
    public string ToolName { get; }

    public string Summary { get; }

    public string? Description { get; }

    public OperationRisk Risk { get; }

    public Type OperationType { get; }

    public Type ArgsType { get; }

    /// <summary>Arguments in position order, then options in declaration order.</summary>
    public IReadOnlyList<OperationParameter> Parameters { get; }

    public IReadOnlyList<OperationExample> Examples { get; }

    public IEnumerable<OperationParameter> Arguments => Parameters.Where(p => p.Kind == ParameterKind.Argument);

    public IEnumerable<OperationParameter> Options => Parameters.Where(p => p.Kind == ParameterKind.Option);

    /// <summary>A fresh copy of the MCP tool input schema (draft 2020-12 object schema, snake_case keys).</summary>
    public JsonObject JsonSchema => (JsonObject)schema.DeepClone();

    /// <summary>One-line synopsis: <c>idiotproof strategy show &lt;id&gt; [--limit &lt;int&gt;] [--active]</c>.</summary>
    public string Usage
    {
        get
        {
            var sb = new StringBuilder(ProgramName).Append(' ').Append(Name);
            foreach (var p in Arguments)
            {
                var token = p.IsArray ? $"<{p.Name}>..." : $"<{p.Name}>";
                sb.Append(' ').Append(p.Required ? token : $"[{token}]");
            }
            foreach (var p in Options)
            {
                var token = p.IsFlag ? $"--{p.Name}" : $"--{p.Name} <{p.TypeLabel}>";
                sb.Append(' ').Append(p.Required ? token : $"[{token}]");
            }
            return sb.ToString();
        }
    }

    /// <summary>Multi-line <c>--help</c> text: summary, usage, parameters, examples.</summary>
    public string Help
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine(Summary);
            if (!string.IsNullOrWhiteSpace(Description)) sb.AppendLine().AppendLine(Description);
            sb.AppendLine().Append("Usage: ").AppendLine(Usage);
            sb.Append("Risk: ").AppendLine(Risk.ToString().ToLowerInvariant());
            if (Parameters.Count > 0)
            {
                sb.AppendLine().AppendLine("Parameters:");
                var labels = Parameters.Select(p => p.Kind == ParameterKind.Argument
                    ? $"<{p.Name}>"
                    : (p.Short is { } s ? $"-{s}, " : "    ") + $"--{p.Name}" + (p.IsFlag ? "" : $" <{p.TypeLabel}>")).ToList();
                var width = labels.Max(l => l.Length);
                for (var i = 0; i < Parameters.Count; i++)
                {
                    var p = Parameters[i];
                    var extra = p.Required ? " (required)" : p.DefaultJson() is { } d ? $" (default: {d.ToJsonString().Trim('"')})" : "";
                    sb.Append("  ").Append(labels[i].PadRight(width)).Append("  ").Append(p.Description).AppendLine(extra);
                }
            }
            if (Examples.Count > 0)
            {
                sb.AppendLine().AppendLine("Examples:");
                foreach (var e in Examples)
                {
                    sb.Append("  ").AppendLine(e.CommandLine);
                    if (!string.IsNullOrWhiteSpace(e.Description)) sb.Append("      ").AppendLine(e.Description);
                }
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>The catalog entry for this operation (what <c>/api/catalog</c> and the docs export contain).</summary>
    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["noun"] = Noun,
        ["verb"] = Verb,
        ["tool_name"] = ToolName,
        ["summary"] = Summary,
        ["description"] = Description,
        ["risk"] = Risk.ToString().ToLowerInvariant(),
        ["usage"] = Usage,
        ["parameters"] = new JsonArray([.. Parameters.Select(p => (JsonNode)new JsonObject
        {
            ["name"] = p.Name,
            ["json_name"] = p.JsonName,
            ["kind"] = p.Kind == ParameterKind.Option ? "option" : "argument",
            ["position"] = p.Position,
            ["short"] = p.Short?.ToString(),
            ["type"] = p.TypeLabel,
            ["required"] = p.Required,
            ["description"] = p.Description,
            ["default"] = p.DefaultJson(),
            ["enum_values"] = p.EnumValues.Count == 0 ? null : new JsonArray([.. p.EnumValues.Select(v => (JsonNode)v)]),
        })]),
        ["examples"] = new JsonArray([.. Examples.Select(e => (JsonNode)new JsonObject
        {
            ["command_line"] = e.CommandLine,
            ["description"] = e.Description,
        })]),
        ["input_schema"] = JsonSchema,
    };

    /// <summary>Runs the operation instance against bound args.</summary>
    internal Task<OperationResult> InvokeAsync(object operation, object args, OperationContext ctx) =>
        invoker(operation, args, ctx);

    /// <summary>
    /// Builds the args record from bound values; parameters not supplied keep the record's own
    /// defaults (property initializers, or primary-constructor parameter defaults).
    /// </summary>
    internal object CreateArgs(IReadOnlyDictionary<OperationParameter, object?> values)
    {
        object instance;
        var assigned = new HashSet<OperationParameter>();
        if (argsConstructor is null || argsConstructor.GetParameters().Length == 0)
        {
            instance = Activator.CreateInstance(ArgsType)!;
        }
        else
        {
            var ctorParams = argsConstructor.GetParameters();
            var ctorArgs = new object?[ctorParams.Length];
            for (var i = 0; i < ctorParams.Length; i++)
            {
                var cp = ctorParams[i];
                var match = Parameters.FirstOrDefault(p => string.Equals(p.Property.Name, cp.Name, StringComparison.OrdinalIgnoreCase));
                if (match is not null && values.TryGetValue(match, out var supplied))
                {
                    ctorArgs[i] = supplied;
                    assigned.Add(match);
                }
                else
                {
                    ctorArgs[i] = CtorDefault(cp);
                    if (match is not null) assigned.Add(match);
                }
            }
            instance = argsConstructor.Invoke(ctorArgs);
        }

        foreach (var (parameter, value) in values)
        {
            if (!assigned.Contains(parameter)) parameter.Property.SetValue(instance, value);
        }
        return instance;
    }

    /// <summary>A constructor parameter's declared default, or default(T) when it has none.</summary>
    internal static object? CtorDefault(ParameterInfo cp)
    {
        if (cp.HasDefaultValue && cp.DefaultValue is not null)
        {
            var target = Nullable.GetUnderlyingType(cp.ParameterType) ?? cp.ParameterType;
            return target.IsEnum ? Enum.ToObject(target, cp.DefaultValue) : cp.DefaultValue;
        }
        return cp.ParameterType.IsValueType && Nullable.GetUnderlyingType(cp.ParameterType) is null
            ? Activator.CreateInstance(cp.ParameterType)
            : null;
    }

    private JsonObject BuildSchema()
    {
        var properties = new JsonObject();
        foreach (var p in Parameters) properties[p.JsonName] = p.ToSchema();
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["description"] = Summary,
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        var required = Parameters.Where(p => p.Required).Select(p => (JsonNode)p.JsonName).ToArray();
        if (required.Length > 0) schema["required"] = new JsonArray(required);
        return schema;
    }

    private static Task<OperationResult> InvokeTyped<TArgs>(object operation, object args, OperationContext ctx)
        where TArgs : class =>
        ((IOperation<TArgs>)operation).ExecuteAsync((TArgs)args, ctx);
}
