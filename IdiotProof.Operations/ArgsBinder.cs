using System.Globalization;
using System.Text.Json;

namespace IdiotProof.Operations;

/// <summary>Outcome of binding: the args record, or every usage problem found (exit 2).</summary>
public sealed record BindResult(object? Args, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;

    /// <summary>All errors on separate lines, followed by the usage synopsis.</summary>
    public string Message(OperationDescriptor descriptor) =>
        string.Join(Environment.NewLine, Errors) + Environment.NewLine + "Usage: " + descriptor.Usage;

    public OperationResult ToUsageResult(OperationDescriptor descriptor) => OperationResult.Usage(Message(descriptor));
}

/// <summary>
/// Turns caller input into an operation's typed args record, from either transport: a JSON object
/// (MCP tool calls and the Hub API — keys may be the option name, its snake_case form, or the
/// property name) or CLI argv tokens (positionals, <c>--name value</c>, <c>--name=value</c>,
/// <c>--flag</c>, <c>--no-flag</c>, <c>-x value</c>, <c>--</c>). Both paths share one value
/// converter, so "bad type" and "bad enum" read the same everywhere. Errors are collected, not
/// thrown, and name the exact parameter and the accepted values.
/// </summary>
public static class ArgsBinder
{
    /// <summary>Binds a JSON object (or null/undefined for "no args").</summary>
    public static BindResult Bind(OperationDescriptor descriptor, JsonElement json)
    {
        var errors = new List<string>();
        var values = new Dictionary<OperationParameter, object?>();

        if (json.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                var parameter = FindByJsonKey(descriptor, property.Name);
                if (parameter is null)
                {
                    errors.Add(UnknownMessage($"'{property.Name}'", descriptor.Parameters.Select(p => p.JsonName), property.Name, quote: true));
                    continue;
                }
                if (values.ContainsKey(parameter))
                {
                    errors.Add($"'{parameter.JsonName}' was given more than once.");
                    continue;
                }
                if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                if (ConvertJson(parameter, property.Value, out var value, out var error)) values[parameter] = value;
                else errors.Add(error!);
            }
        }
        else if (json.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            errors.Add($"Arguments must be a JSON object, got {json.ValueKind.ToString().ToLowerInvariant()}.");
        }

        return Finish(descriptor, values, errors, quoteJson: true);
    }

    /// <summary>Binds JSON text (convenience over <see cref="Bind(OperationDescriptor, JsonElement)"/>).</summary>
    public static BindResult BindJson(OperationDescriptor descriptor, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Bind(descriptor, default(JsonElement));
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Bind(descriptor, doc.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            return new BindResult(null, [$"Arguments are not valid JSON: {ex.Message}"]);
        }
    }

    /// <summary>Binds CLI tokens that follow the operation name (global flags already removed).</summary>
    public static BindResult Bind(OperationDescriptor descriptor, IReadOnlyList<string> argv)
    {
        var errors = new List<string>();
        var raw = new Dictionary<OperationParameter, List<string>>();
        var positionals = new List<string>();
        var optionsEnded = false;

        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i];
            if (optionsEnded || !ArgvParser.IsOptionToken(token))
            {
                positionals.Add(token);
                continue;
            }
            if (token == "--")
            {
                optionsEnded = true;
                continue;
            }

            OperationParameter? option;
            string? inline;
            string spelled;
            var negated = false;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                (var name, inline) = ArgvParser.SplitInline(token[2..]);
                spelled = "--" + name;
                option = FindOption(descriptor, name);
                if (option is null && name.StartsWith("no-", StringComparison.Ordinal)
                    && FindOption(descriptor, name[3..]) is { IsFlag: true } negatable && inline is null)
                {
                    option = negatable;
                    negated = true;
                }
            }
            else if (token.Length == 2)
            {
                spelled = token;
                inline = null;
                option = descriptor.Options.FirstOrDefault(p => p.Short == token[1]);
            }
            else
            {
                errors.Add($"Unknown option '{token}' (short options are single letters; use --name for long ones).");
                continue;
            }

            if (option is null)
            {
                errors.Add(UnknownMessage($"'{spelled}'", descriptor.Options.Select(p => "--" + p.Name), spelled, quote: true));
                continue;
            }

            string value;
            if (option.IsFlag) value = negated ? "false" : inline ?? "true";
            else if (inline is not null) value = inline;
            else if (i + 1 < argv.Count && !ArgvParser.IsLongOptionToken(argv[i + 1])) value = argv[++i];
            else
            {
                errors.Add($"Option --{option.Name} needs a value ({option.TypeLabel}).");
                continue;
            }

            if (!raw.TryGetValue(option, out var list)) raw[option] = list = [];
            if (list.Count > 0 && !option.IsArray)
            {
                errors.Add($"Option --{option.Name} was given more than once.");
                continue;
            }
            list.Add(value);
        }

        // Positionals fill arguments in order; a trailing string[] argument takes all that remain.
        var arguments = descriptor.Arguments.ToList();
        for (var p = 0; p < positionals.Count; p++)
        {
            if (p < arguments.Count && !arguments[p].IsArray)
            {
                raw[arguments[p]] = [positionals[p]];
            }
            else if (arguments.Count > 0 && arguments[^1].IsArray && p >= arguments.Count - 1)
            {
                raw[arguments[^1]] = [.. positionals.Skip(arguments.Count - 1)];
                break;
            }
            else
            {
                errors.Add(arguments.Count == 0
                    ? $"Unexpected argument '{positionals[p]}' ({descriptor.Name} takes no positional arguments)."
                    : $"Unexpected argument '{positionals[p]}' (expected at most {arguments.Count}).");
                break;
            }
        }

        var values = new Dictionary<OperationParameter, object?>();
        foreach (var (parameter, texts) in raw)
        {
            if (parameter.IsArray)
            {
                values[parameter] = texts
                    .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToArray();
            }
            else if (ConvertText(parameter, texts[0], out var value, out var error, quoteJson: false))
            {
                values[parameter] = value;
            }
            else
            {
                errors.Add(error!);
            }
        }

        return Finish(descriptor, values, errors, quoteJson: false);
    }

    // ── shared ──

    private static BindResult Finish(
        OperationDescriptor descriptor, Dictionary<OperationParameter, object?> values, List<string> errors, bool quoteJson)
    {
        foreach (var missing in descriptor.Parameters.Where(p => p.Required && !values.ContainsKey(p)))
        {
            errors.Add(quoteJson
                ? $"Missing required '{missing.JsonName}' ({missing.TypeLabel})."
                : missing.Kind == ParameterKind.Argument
                    ? $"Missing required argument <{missing.Name}>."
                    : $"Missing required option --{missing.Name} <{missing.TypeLabel}>.");
        }
        if (errors.Count > 0) return new BindResult(null, errors);

        try
        {
            return new BindResult(descriptor.CreateArgs(values), []);
        }
        catch (Exception ex)
        {
            // A record constructor that validates its own inputs reports a usage error, not a crash.
            var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } ie } ? ie : ex;
            return new BindResult(null, [inner.Message]);
        }
    }

    private static OperationParameter? FindOption(OperationDescriptor descriptor, string name) =>
        descriptor.Options.FirstOrDefault(p => p.Name == name)
        ?? descriptor.Options.FirstOrDefault(p => OperationNames.Fold(p.Name) == OperationNames.Fold(name));

    private static OperationParameter? FindByJsonKey(OperationDescriptor descriptor, string key)
    {
        var folded = OperationNames.Fold(key);
        return descriptor.Parameters.FirstOrDefault(p =>
            OperationNames.Fold(p.Name) == folded || OperationNames.Fold(p.Property.Name) == folded);
    }

    private static string UnknownMessage(string spelled, IEnumerable<string> known, string typed, bool quote)
    {
        var candidates = known.ToList();
        var suggestion = OperationNames.Suggest(typed.TrimStart('-'), candidates.Select(c => c.TrimStart('-')));
        var hint = suggestion is not null
            ? $" Did you mean {candidates.First(c => c.TrimStart('-') == suggestion)}?"
            : candidates.Count == 0 ? " This operation takes no options." : $" Known: {string.Join(", ", candidates)}.";
        return $"Unknown option {spelled}.{hint}";
    }

    /// <summary>Converts a JSON value: native JSON types where they fit, otherwise the text path.</summary>
    private static bool ConvertJson(OperationParameter p, JsonElement element, out object? value, out string? error)
    {
        error = null;
        switch (p.ValueKind)
        {
            case ParameterValueKind.StringArray:
                if (element.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                        {
                            value = null;
                            error = $"'{p.JsonName}' must be an array of strings.";
                            return false;
                        }
                        items.Add(item.GetString()!);
                    }
                    value = items.ToArray();
                    return true;
                }
                if (element.ValueKind == JsonValueKind.String)
                {
                    // A lone string is a one-item list (or a comma list, matching the CLI).
                    value = element.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return true;
                }
                break;
            case ParameterValueKind.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case ParameterValueKind.Int32 or ParameterValueKind.Int64 or ParameterValueKind.Decimal or ParameterValueKind.Double
                when element.ValueKind == JsonValueKind.Number:
                return ConvertText(p, element.GetRawText(), out value, out error, quoteJson: true);
            default:
                if (element.ValueKind == JsonValueKind.String)
                    return ConvertText(p, element.GetString()!, out value, out error, quoteJson: true);
                break;
        }

        // Some MCP clients stringify everything; tolerate a number or bool sent where text is expected.
        if (p.ValueKind == ParameterValueKind.String && element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetRawText();
            return true;
        }

        value = null;
        error = $"'{p.JsonName}' expects {Expectation(p)}, got {element.ValueKind.ToString().ToLowerInvariant()}.";
        return false;
    }

    /// <summary>Converts one text value (an argv token or a JSON string) to the parameter's type.</summary>
    private static bool ConvertText(OperationParameter p, string text, out object? value, out string? error, bool quoteJson)
    {
        var label = quoteJson ? $"'{p.JsonName}'" : p.Kind == ParameterKind.Argument ? $"<{p.Name}>" : $"--{p.Name}";
        var inv = CultureInfo.InvariantCulture;
        error = null;
        value = null;
        var ok = true;
        switch (p.ValueKind)
        {
            case ParameterValueKind.String: value = text; break;
            case ParameterValueKind.StringArray: value = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
            case ParameterValueKind.Boolean:
                ok = TryBool(text, out var b);
                value = b;
                break;
            case ParameterValueKind.Int32:
                ok = int.TryParse(text, NumberStyles.Integer, inv, out var i32);
                value = i32;
                break;
            case ParameterValueKind.Int64:
                ok = long.TryParse(text, NumberStyles.Integer, inv, out var i64);
                value = i64;
                break;
            case ParameterValueKind.Decimal:
                // Commas only as real thousands groups ("1,250.50"): "0,25" typed with a comma
                // decimal must be refused, not silently read as 25 (a 100x order or withdrawal).
                var dec = 0m;
                ok = ThousandsOk(StripMoney(text)) && decimal.TryParse(StripMoney(text), NumberStyles.Float | NumberStyles.AllowThousands, inv, out dec);
                value = dec;
                break;
            case ParameterValueKind.Double:
                var dbl = 0d;
                ok = ThousandsOk(StripMoney(text)) && double.TryParse(StripMoney(text), NumberStyles.Float | NumberStyles.AllowThousands, inv, out dbl) && double.IsFinite(dbl);
                value = dbl;
                break;
            case ParameterValueKind.Guid:
                ok = Guid.TryParse(text, out var guid);
                value = guid;
                break;
            case ParameterValueKind.Date:
                ok = DateOnly.TryParseExact(text, ["yyyy-MM-dd", "yyyy-M-d"], inv, DateTimeStyles.None, out var date);
                value = date;
                break;
            case ParameterValueKind.Time:
                ok = TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "h:mm tt", "h:mmtt", "h:mm:ss tt"], inv, DateTimeStyles.None, out var time);
                value = time;
                break;
            case ParameterValueKind.Enum:
                var match = Enum.GetValues(p.ValueType).Cast<object>().FirstOrDefault(v =>
                    OperationNames.Fold(OperationParameter.EnumWireName(v)) == OperationNames.Fold(text));
                ok = match is not null;
                value = match;
                if (!ok)
                {
                    error = $"{label} must be one of {string.Join(", ", p.EnumValues)}; got '{text}'.";
                    return false;
                }
                break;
        }
        if (!ok)
        {
            value = null;
            error = $"{label} expects {Expectation(p)}, got '{text}'.";
        }
        return ok;
    }

    private static string Expectation(OperationParameter p) => p.ValueKind switch
    {
        ParameterValueKind.Boolean => "true or false",
        ParameterValueKind.Int32 => "a whole number",
        ParameterValueKind.Int64 => "a whole number",
        ParameterValueKind.Decimal or ParameterValueKind.Double => "a number",
        ParameterValueKind.Guid => "a GUID",
        ParameterValueKind.Date => "a date (yyyy-MM-dd)",
        ParameterValueKind.Time => "an ET time of day (HH:mm, e.g. 09:30 or 15:55)",
        ParameterValueKind.Enum => "one of " + string.Join(", ", p.EnumValues),
        ParameterValueKind.StringArray => "a list of strings",
        _ => "text",
    };

    private static bool TryBool(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "1" or "on": value = true; return true;
            case "false" or "no" or "0" or "off": value = false; return true;
            default: value = false; return false;
        }
    }

    /// <summary>Lets <c>$1,250.50</c> through as a number (people paste prices).</summary>
    /// <summary>No comma at all, or commas only between groups of exactly three digits before any decimal point.</summary>
    private static bool ThousandsOk(string text) =>
        !text.Contains(',') || System.Text.RegularExpressions.Regex.IsMatch(text, @"^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$");

    private static string StripMoney(string text) => text.Trim().Replace("$", "", StringComparison.Ordinal);
}
