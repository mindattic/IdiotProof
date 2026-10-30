using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdiotProof.Operations;

/// <summary>Thrown when the reflected operations break a catalog rule; lists every problem at once.</summary>
public sealed class OperationCatalogException(IReadOnlyList<string> problems)
    : Exception("Invalid operation catalog:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "  - " + p)))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Every operation the process can run, reflected once at startup. Construction validates the
/// whole set — unique names, a summary on every operation, a description on every parameter,
/// kebab-case names, no positional gaps, supported types — and fails fast with every problem, so a
/// half-documented operation never reaches the CLI, the MCP tool list or the docs.
/// </summary>
public sealed class OperationCatalog
{
    /// <summary>
    /// Flags the front doors own (output mode, owner, confirmation, help, Hub address); an
    /// operation may not declare them.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedOptionNames =
        new HashSet<string>(StringComparer.Ordinal) { "help", "json", "user", "confirm", "hub", "color", "no-color" };

    /// <summary>Short aliases the front doors own (<c>-h</c> for help).</summary>
    public static readonly IReadOnlySet<char> ReservedShorts = new HashSet<char> { 'h' };

    private readonly Dictionary<string, OperationDescriptor> byName;
    private readonly Dictionary<string, OperationDescriptor> byToolName;

    private OperationCatalog(IReadOnlyList<OperationDescriptor> operations)
    {
        Operations = operations;
        byName = operations.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
        byToolName = operations.ToDictionary(o => o.ToolName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All operations, sorted by name.</summary>
    public IReadOnlyList<OperationDescriptor> Operations { get; }

    /// <summary>Reflects every <see cref="OperationAttribute"/> class in the assemblies; throws <see cref="OperationCatalogException"/> on any problem.</summary>
    public static OperationCatalog FromAssemblies(params Assembly[] assemblies) =>
        FromTypes(DiscoverTypes(assemblies));

    /// <summary>Builds from an explicit type list (tests, or hosts that register a subset).</summary>
    public static OperationCatalog FromTypes(IEnumerable<Type> types)
    {
        var (descriptors, problems) = Build(types);
        if (problems.Count > 0) throw new OperationCatalogException(problems);
        return new OperationCatalog(descriptors);
    }

    /// <summary>Every rule violation in the given types, without throwing (empty = valid).</summary>
    public static IReadOnlyList<string> Validate(IEnumerable<Type> types) => Build(types).Problems;

    /// <summary>
    /// Candidate types in the assemblies: anything carrying <see cref="OperationAttribute"/> or
    /// implementing <see cref="IOperation{TArgs}"/> (so a forgotten attribute is reported, not silently skipped).
    /// </summary>
    public static IEnumerable<Type> DiscoverTypes(params Assembly[] assemblies) =>
        assemblies.Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition
                && (t.IsDefined(typeof(OperationAttribute), false) || OperationArgsType(t, out _) > 0));

    /// <summary>Looks up by <c>"noun verb"</c> or by tool name (<c>noun_verb</c>), case-insensitively.</summary>
    public OperationDescriptor? Find(string name)
    {
        var trimmed = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return byName.GetValueOrDefault(trimmed) ?? byToolName.GetValueOrDefault(trimmed);
    }

    /// <summary>
    /// Matches the longest operation name at the start of a CLI argv (<c>["strategy", "list", "--active"]</c>
    /// → <c>strategy list</c>, rest <c>["--active"]</c>).
    /// </summary>
    public bool TryResolve(IReadOnlyList<string> argv, out OperationDescriptor? descriptor, out IReadOnlyList<string> rest)
    {
        for (var take = Math.Min(argv.Count, 4); take >= 1; take--)
        {
            if (argv.Take(take).Any(t => t.StartsWith('-'))) continue;
            if (byName.TryGetValue(string.Join(' ', argv.Take(take)), out var found))
            {
                descriptor = found;
                rest = argv.Skip(take).ToArray();
                return true;
            }
        }
        descriptor = null;
        rest = argv;
        return false;
    }

    /// <summary>The closest operation name to what was typed, for "did you mean" messages.</summary>
    public string? Suggest(string name) => OperationNames.Suggest(name, Operations.Select(o => o.Name));

    /// <summary>The whole catalog as JSON — what <c>/api/catalog</c>, <c>agent catalog</c> and the docs export emit.</summary>
    public JsonObject ToJsonNode() => new()
    {
        ["program"] = OperationDescriptor.ProgramName,
        ["operations"] = new JsonArray([.. Operations.Select(o => (JsonNode)o.ToJson())]),
    };

    public string ToJson(bool indented = true) =>
        ToJsonNode().ToJsonString(indented ? OperationJson.Indented : OperationJson.Options);

    // ── reflection + validation ──

    private static (List<OperationDescriptor> Descriptors, List<string> Problems) Build(IEnumerable<Type> types)
    {
        var problems = new List<string>();
        var descriptors = new List<OperationDescriptor>();
        foreach (var type in types.Distinct())
        {
            if (TryDescribe(type, problems) is { } d) descriptors.Add(d);
        }

        foreach (var dupe in descriptors.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"Operation name '{dupe.Key}' is used by {string.Join(", ", dupe.Select(d => d.OperationType.FullName))}.");
        foreach (var dupe in descriptors.GroupBy(d => d.ToolName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                     .Where(g => g.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            problems.Add($"Tool name '{dupe.Key}' collides between {string.Join(", ", dupe.Select(d => $"'{d.Name}'"))}.");

        return ([.. descriptors.OrderBy(d => d.Name, StringComparer.Ordinal)], problems);
    }

    /// <summary>How many <see cref="IOperation{TArgs}"/> the type implements (the one args type when exactly one).</summary>
    private static int OperationArgsType(Type type, out Type? argsType)
    {
        var interfaces = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IOperation<>))
            .ToArray();
        argsType = interfaces.Length == 1 ? interfaces[0].GetGenericArguments()[0] : null;
        return interfaces.Length;
    }

    private static OperationDescriptor? TryDescribe(Type type, List<string> problems)
    {
        var where = type.FullName ?? type.Name;
        var attribute = type.GetCustomAttribute<OperationAttribute>(inherit: false);
        var count = OperationArgsType(type, out var argsType);
        if (attribute is null)
        {
            problems.Add($"{where} implements IOperation<> but has no [Operation] attribute.");
            return null;
        }
        if (count != 1 || argsType is null)
        {
            problems.Add($"{where}: an [Operation] class must implement exactly one IOperation<TArgs> (found {count}).");
            return null;
        }
        if (type.IsAbstract || type.IsGenericTypeDefinition)
        {
            problems.Add($"{where}: an operation must be a concrete, non-generic class.");
            return null;
        }

        var before = problems.Count;
        var name = attribute.Name ?? "";
        var words = name.Split(' ');
        if (words.Length < 2 || words.Any(w => !OperationNames.IsKebab(w)))
            problems.Add($"{where}: operation name '{name}' must be two or more lower kebab-case words (\"noun verb\").");
        if (string.IsNullOrWhiteSpace(attribute.Summary))
            problems.Add($"'{name}' ({where}) has no Summary.");

        var (parameters, ctor) = DescribeArgs(name, argsType, problems);

        var examples = type.GetCustomAttributes<ExampleAttribute>(inherit: false)
            .Select(e => new OperationExample(e.CommandLine, e.Description))
            .ToList();
        foreach (var e in examples)
        {
            var prefix = $"{OperationDescriptor.ProgramName} {name}";
            if (e.CommandLine != prefix && !e.CommandLine.StartsWith(prefix + " ", StringComparison.Ordinal))
                problems.Add($"'{name}': example '{e.CommandLine}' must start with '{prefix}'.");
        }

        if (problems.Count > before) return null;
        try
        {
            return new OperationDescriptor(attribute, type, argsType, parameters, examples, ctor);
        }
        catch (Exception ex)
        {
            problems.Add($"'{name}' ({where}): {ex.Message}");
            return null;
        }
    }

    private static (List<OperationParameter> Parameters, ConstructorInfo? Ctor) DescribeArgs(
        string op, Type argsType, List<string> problems)
    {
        var parameters = new List<OperationParameter>();
        var ctors = argsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var parameterless = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
        var props = argsType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Positional records bind through their primary constructor: every parameter must be a property.
        var ctor = parameterless ?? ctors
            .Where(c => c.GetParameters().All(p => props.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null)
        {
            problems.Add($"'{op}': args type {argsType.Name} needs a public parameterless constructor or a primary constructor whose parameters are its properties.");
            return (parameters, null);
        }

        // Defaults come from a default-constructed instance, or from the primary constructor's parameter defaults.
        object? sample = null;
        if (ctor.GetParameters().Length == 0)
        {
            try { sample = ctor.Invoke(null); } catch { /* defaults unknown; not fatal */ }
        }

        foreach (var prop in props.Where(p => p.DeclaringType != typeof(object) && p.Name != "EqualityContract"))
        {
            var option = prop.GetCustomAttribute<OptionAttribute>();
            var argument = prop.GetCustomAttribute<ArgumentAttribute>();
            if (option is null && argument is null)
            {
                if (prop.CanWrite) problems.Add($"'{op}': property {argsType.Name}.{prop.Name} has neither [Option] nor [Argument].");
                continue;
            }
            if (option is not null && argument is not null)
            {
                problems.Add($"'{op}': property {prop.Name} cannot be both an option and an argument.");
                continue;
            }
            if (!prop.CanWrite && ctor.GetParameters().All(p => !string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"'{op}': property {prop.Name} is not settable.");
                continue;
            }

            var valueType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (Classify(valueType) is not { } kind)
            {
                problems.Add($"'{op}': property {prop.Name} has unsupported type {prop.PropertyType.Name}.");
                continue;
            }

            object? defaultValue = sample is not null
                ? prop.GetValue(sample)
                : ctor.GetParameters().FirstOrDefault(p => string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase)) is { } cp
                    ? OperationDescriptor.CtorDefault(cp)
                    : null;

            var name = option?.Name ?? argument!.Name ?? OperationNames.ToKebab(prop.Name);
            var description = option?.Description ?? argument!.Description;
            var shortName = option is { Short: not '\0' } ? option.Short : (char?)null;

            if (!OperationNames.IsKebab(name)) problems.Add($"'{op}': parameter name '{name}' must be lower kebab-case.");
            if (string.IsNullOrWhiteSpace(description)) problems.Add($"'{op}': parameter '{name}' has no Description.");
            if (option is not null && ReservedOptionNames.Contains(name)) problems.Add($"'{op}': option '--{name}' is reserved for the front doors.");
            if (option is not null && name.StartsWith("no-", StringComparison.Ordinal)) problems.Add($"'{op}': option '--{name}' may not start with 'no-' (that prefix negates flags).");
            if (shortName is { } s && (!char.IsAsciiLetter(s) || ReservedShorts.Contains(s))) problems.Add($"'{op}': short alias '-{s}' must be an ASCII letter other than -h.");

            parameters.Add(new OperationParameter
            {
                Name = name,
                Kind = option is not null ? ParameterKind.Option : ParameterKind.Argument,
                Position = argument?.Position,
                Short = shortName,
                Description = description ?? "",
                Required = option?.Required ?? argument!.Required,
                ValueKind = kind,
                ValueType = valueType,
                Property = prop,
                DefaultValue = defaultValue,
                EnumValues = valueType.IsEnum ? [.. Enum.GetValues(valueType).Cast<object>().Select(OperationParameter.EnumWireName)] : [],
            });
        }

        foreach (var dupe in parameters.GroupBy(p => OperationNames.Fold(p.Name)).Where(g => g.Count() > 1))
            problems.Add($"'{op}': parameter names collide: {string.Join(", ", dupe.Select(p => p.Name))}.");
        foreach (var dupe in parameters.Where(p => p.Short is not null).GroupBy(p => p.Short).Where(g => g.Count() > 1))
            problems.Add($"'{op}': short alias '-{dupe.Key}' is used more than once.");

        var arguments = parameters.Where(p => p.Kind == ParameterKind.Argument).OrderBy(p => p.Position).ToList();
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Position != i)
            {
                problems.Add($"'{op}': argument positions must run 0..{arguments.Count - 1} with no gaps or repeats (found {string.Join(", ", arguments.Select(a => a.Position))}).");
                break;
            }
        }
        for (var i = 1; i < arguments.Count; i++)
        {
            if (arguments[i].Required && !arguments[i - 1].Required)
                problems.Add($"'{op}': required argument <{arguments[i].Name}> cannot follow optional <{arguments[i - 1].Name}>.");
        }
        foreach (var a in arguments.Where(a => a.IsArray).Where(a => a != arguments[^1]))
            problems.Add($"'{op}': string[] argument <{a.Name}> must be the last argument.");

        // Arguments first (by position), then options in declaration order.
        var ordered = arguments.Concat(parameters.Where(p => p.Kind == ParameterKind.Option)).ToList();
        return (ordered, ctor);
    }

    private static ParameterValueKind? Classify(Type t) => t switch
    {
        _ when t == typeof(string) => ParameterValueKind.String,
        _ when t == typeof(bool) => ParameterValueKind.Boolean,
        _ when t == typeof(int) => ParameterValueKind.Int32,
        _ when t == typeof(long) => ParameterValueKind.Int64,
        _ when t == typeof(decimal) => ParameterValueKind.Decimal,
        _ when t == typeof(double) => ParameterValueKind.Double,
        _ when t == typeof(Guid) => ParameterValueKind.Guid,
        _ when t == typeof(DateOnly) => ParameterValueKind.Date,
        _ when t == typeof(TimeOnly) => ParameterValueKind.Time,
        _ when t == typeof(string[]) => ParameterValueKind.StringArray,
        { IsEnum: true } => ParameterValueKind.Enum,
        _ => null,
    };
}
