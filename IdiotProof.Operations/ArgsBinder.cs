using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IdiotProof.Operations;

/// <summary>
/// Outcome of binding: the args record, or every usage problem found (exit 2). On failure
/// <see cref="Problems"/> holds the same errors with the correction each one carries, and
/// <see cref="Fix"/> the whole corrected call when every problem had one (IP-T523).
/// </summary>
public sealed record BindResult(object? Args, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;

    /// <summary>The errors as data: message, parameter and, when determinable, the correction.</summary>
    public IReadOnlyList<BindError> Problems { get; init; } = [];

    /// <summary>The corrected call, when every problem had a correction and it bound cleanly; never run for the caller.</summary>
    public CallFix? Fix { get; init; }

    /// <summary>The bound values (after corrections, when there is a <see cref="Fix"/>); null when the call can't be completed.</summary>
    internal IReadOnlyDictionary<OperationParameter, object?>? Values { get; init; }

    /// <summary>All errors on separate lines, followed by the usage synopsis.</summary>
    public string Message(OperationDescriptor descriptor) =>
        string.Join(Environment.NewLine, Errors) + Environment.NewLine + "Usage: " + descriptor.Usage;

    /// <summary>
    /// Exit 2 with the problems and the corrected call as data. The corrected call is printed from
    /// <see cref="OperationResult.Human"/>, not the message: it repeats every argument value, and
    /// the message is what the command ledger and the Hub window keep.
    /// </summary>
    public OperationResult ToUsageResult(OperationDescriptor descriptor) =>
        OperationResult.Usage(Message(descriptor), new UsageErrorData(descriptor.Name, Problems, Fix, descriptor.Usage))
            with { Human = Fix is null ? null : ArgsBinder.FixLines(Fix) };
}

/// <summary>
/// Turns caller input into an operation's typed args record, from either transport: a JSON object
/// (MCP tool calls and the Hub API — keys may be the option name, its snake_case form, or the
/// property name) or CLI argv tokens (positionals, <c>--name value</c>, <c>--name=value</c>,
/// <c>--flag</c>, <c>--no-flag</c>, <c>-x value</c>, <c>--</c>). Both paths share one value
/// converter, so "bad type" and "bad enum" read the same everywhere. Errors are collected, not
/// thrown, and name the exact parameter and the accepted values.
/// <para>
/// Each error also carries its correction as data when exactly one exists (IP-T523): a misspelled
/// option or JSON key with a single closest name, a positional argument passed as <c>--name</c>,
/// an enum value one typo from a single accepted value, a whole number written as <c>5.0</c> or
/// <c>1_000</c>, a date in another unambiguous form (<c>2026/10/30</c>, <c>Oct 30 2026</c>,
/// <c>30/10/2026</c>), a time as <c>0930</c> or <c>9am</c>, <c>y</c>/<c>n</c> for a flag. When every
/// error has one, the corrected call is bound again and returned as a <see cref="CallFix"/>. No
/// correction is offered for a missing required value, a word where a number belongs, a date such
/// as <c>03/04/2026</c>, or any number containing a comma: "1,005" stays refused, never read as
/// one meaning.
/// </para>
/// </summary>
public static partial class ArgsBinder
{
    // ── the accepted forms, as data ──
    // The converters below read these tables and ValueFormats publishes them (agent catalog,
    // IP-T526), so what the catalog says a date, a time, a flag or a number may look like is what
    // the binder accepts.

    /// <summary>The date forms the binder reads.</summary>
    internal static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-M-d"];

    /// <summary>Other date forms with one reading: refused, with the ISO date offered as the correction.</summary>
    internal static readonly string[] DateRepairFormats =
    [
        "yyyy/M/d", "yyyy.M.d", "yyyyMMdd", "MMM d yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMMM d, yyyy", "d MMM yyyy", "d MMMM yyyy",
    ];

    /// <summary>The ET time-of-day forms the binder reads.</summary>
    internal static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "h:mm tt", "h:mmtt", "h:mm:ss tt"];

    /// <summary>Four digits (0930): refused, with HH:mm offered as the correction.</summary>
    internal const string TimeDigitsFormat = "HHmm";

    /// <summary>An hour with am/pm (9am, 9 am): refused, with HH:mm offered as the correction.</summary>
    internal static readonly string[] TimeMeridiemFormats = ["h tt", "htt"];

    /// <summary>The words read as true and as false (case-insensitive).</summary>
    internal static readonly string[] TrueWords = ["true", "yes", "1", "on"];

    internal static readonly string[] FalseWords = ["false", "no", "0", "off"];

    /// <summary>One-letter flags: refused, with true/false offered as the correction.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BoolShorthand =
        new Dictionary<string, string> { ["y"] = "true", ["t"] = "true", ["n"] = "false", ["f"] = "false" };

    /// <summary>The only shape in which a number may contain commas: thousands groups AND a decimal point.</summary>
    internal const string ThousandsPattern = @"^[+-]?[1-9]\d{0,2}(,\d{3})+\.\d+$";

    /// <summary>Binds a JSON object (or null/undefined for "no args").</summary>
    public static BindResult Bind(OperationDescriptor descriptor, JsonElement json) => Bind(descriptor, json, withFix: true);

    private static BindResult Bind(OperationDescriptor descriptor, JsonElement json, bool withFix)
    {
        var errors = new List<BindError>();
        var values = new Dictionary<OperationParameter, object?>();

        if (json.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                var parameter = FindByJsonKey(descriptor, property.Name);
                CallChange? rename = null;
                if (parameter is null)
                {
                    var message = UnknownMessage($"'{property.Name}'", descriptor.Parameters.Select(p => p.JsonName), property.Name);
                    var (closest, _, unique) = OperationNames.Closest(property.Name, descriptor.Parameters.Select(p => p.JsonName));
                    parameter = closest is not null && unique ? descriptor.Parameters.First(p => p.JsonName == closest) : null;
                    if (parameter is null || values.ContainsKey(parameter))
                    {
                        errors.Add(new BindError(message));
                        continue;
                    }
                    rename = new CallChange(CallChangeKinds.Option, parameter.JsonName, property.Name, parameter.JsonName);
                    errors.Add(new BindError(message, null, rename));
                }
                else if (values.ContainsKey(parameter))
                {
                    errors.Add(new BindError($"'{parameter.JsonName}' was given more than once.", parameter.JsonName));
                    continue;
                }
                if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                if (ConvertJson(parameter, property.Value, out var value, out var error))
                {
                    values[parameter] = value;
                    continue;
                }
                var sent = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => null,
                };
                errors.Add(Unreadable(parameter, sent, error!, values, quoteJson: true));
            }
        }
        else if (json.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            errors.Add(new BindError($"Arguments must be a JSON object, got {json.ValueKind.ToString().ToLowerInvariant()}."));
        }

        return Finish(descriptor, values, errors, quoteJson: true, withFix);
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
            var message = $"Arguments are not valid JSON: {ex.Message}";
            return new BindResult(null, [message]) { Problems = [new BindError(message)] };
        }
    }

    /// <summary>Binds CLI tokens that follow the operation name (global flags already removed).</summary>
    public static BindResult Bind(OperationDescriptor descriptor, IReadOnlyList<string> argv) => Bind(descriptor, argv, withFix: true);

    private static BindResult Bind(OperationDescriptor descriptor, IReadOnlyList<string> argv, bool withFix)
    {
        var errors = new List<BindError>();
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
                errors.Add(new BindError($"Unknown option '{token}' (short options are single letters; use --name for long ones)."));
                continue;
            }

            if (option is null)
            {
                var message = UnknownMessage($"'{spelled}'", descriptor.Options.Select(p => "--" + p.Name), spelled);
                var typed = spelled.TrimStart('-');

                // A positional argument passed by name (--ticker AAPL): the value is the argument.
                var folded = OperationNames.Fold(typed);
                var argument = spelled.Length > 2 ? descriptor.Arguments.FirstOrDefault(a =>
                    OperationNames.Fold(a.Name) == folded || OperationNames.Fold(a.Property.Name) == folded) : null;
                if (argument is not null)
                {
                    var given = inline ?? (i + 1 < argv.Count && !ArgvParser.IsLongOptionToken(argv[i + 1]) ? argv[++i] : null);
                    if (given is null || (raw.ContainsKey(argument) && !argument.IsArray))
                    {
                        errors.Add(new BindError(message));
                        continue;
                    }
                    if (!raw.TryGetValue(argument, out var named)) raw[argument] = named = [];
                    named.Add(given);
                    errors.Add(new BindError(
                        $"{message} <{argument.Name}> is a positional argument: pass the value without {spelled}.",
                        argument.JsonName, new CallChange(CallChangeKinds.Argument, argument.JsonName, spelled, $"<{argument.Name}>")));
                    continue;
                }

                var (closest, _, unique) = spelled.Length > 2
                    ? OperationNames.Closest(typed, descriptor.Options.Select(p => p.Name))
                    : (null, 0, false);
                option = closest is not null && unique ? descriptor.Options.First(p => p.Name == closest) : null;
                if (option is null)
                {
                    errors.Add(new BindError(message));
                    continue;
                }
                errors.Add(new BindError(message, null, new CallChange(CallChangeKinds.Option, option.JsonName, spelled, "--" + option.Name)));
            }

            string value;
            if (option.IsFlag) value = negated ? "false" : inline ?? "true";
            else if (inline is not null) value = inline;
            else if (i + 1 < argv.Count && !ArgvParser.IsLongOptionToken(argv[i + 1])) value = argv[++i];
            else
            {
                errors.Add(new BindError($"Option --{option.Name} needs a value ({option.TypeLabel}).", option.JsonName));
                continue;
            }

            if (!raw.TryGetValue(option, out var list)) raw[option] = list = [];
            if (list.Count > 0 && !option.IsArray)
            {
                errors.Add(new BindError($"Option --{option.Name} was given more than once.", option.JsonName));
                continue;
            }
            list.Add(value);
        }

        // Positionals fill arguments in order (skipping any already passed by name); a trailing
        // string[] argument takes all that remain.
        var arguments = descriptor.Arguments.Where(a => !raw.ContainsKey(a)).ToList();
        var declared = descriptor.Arguments.Count();
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
                errors.Add(new BindError(declared == 0
                    ? $"Unexpected argument '{positionals[p]}' ({descriptor.Name} takes no positional arguments)."
                    : $"Unexpected argument '{positionals[p]}' (expected at most {declared})."));
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
                errors.Add(Unreadable(parameter, texts[0], error!, values, quoteJson: false));
            }
        }

        return Finish(descriptor, values, errors, quoteJson: false, withFix);
    }

    // ── shared ──

    private static BindResult Finish(
        OperationDescriptor descriptor, Dictionary<OperationParameter, object?> values, List<BindError> errors, bool quoteJson, bool withFix)
    {
        foreach (var missing in descriptor.Parameters.Where(p => p.Required && !values.ContainsKey(p)))
        {
            // No correction: only the caller knows the value.
            errors.Add(new BindError(quoteJson
                ? $"Missing required '{missing.JsonName}' ({missing.TypeLabel})."
                : missing.Kind == ParameterKind.Argument
                    ? $"Missing required argument <{missing.Name}>."
                    : $"Missing required option --{missing.Name} <{missing.TypeLabel}>.", missing.JsonName));
        }
        if (errors.Count > 0)
        {
            var fix = withFix && errors.All(e => e.Fix is not null)
                ? BuildFix(descriptor, values, [.. errors.Select(e => e.Fix!)])
                : null;
            return new BindResult(null, [.. errors.Select(e => e.Message)])
            {
                Problems = errors,
                Fix = fix,
                Values = fix is null ? null : values,
            };
        }

        try
        {
            return new BindResult(descriptor.CreateArgs(values), []) { Values = values };
        }
        catch (Exception ex)
        {
            // A record constructor that validates its own inputs reports a usage error, not a crash.
            var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } ie } ? ie : ex;
            return new BindResult(null, [inner.Message]) { Problems = [new BindError(inner.Message)] };
        }
    }

    /// <summary>
    /// The error for a value that didn't convert, with its correction when the text has exactly one
    /// reading in the parameter's type (the corrected value is then bound, so a later check sees it).
    /// </summary>
    private static BindError Unreadable(
        OperationParameter parameter, string? sent, string error, Dictionary<OperationParameter, object?> values, bool quoteJson)
    {
        if (sent is not null && RepairText(parameter, sent) is { } repaired
            && ConvertText(parameter, repaired, out var value, out _, quoteJson))
        {
            values[parameter] = value;
            return new BindError(error, parameter.JsonName, new CallChange(CallChangeKinds.Value, parameter.JsonName, sent, repaired));
        }
        return new BindError(error, parameter.JsonName);
    }

    /// <summary>
    /// The one accepted spelling of a value that failed to convert, or null when there is none or
    /// more than one. Anything with a comma in a number is left alone: it is ambiguous by rule.
    /// </summary>
    internal static string? RepairText(OperationParameter p, string text)
    {
        var inv = CultureInfo.InvariantCulture;
        var t = text.Trim();
        switch (p.ValueKind)
        {
            case ParameterValueKind.Int32 or ParameterValueKind.Int64:
                // 5.0, 1e3 or 1_000 is one whole number; 1.5 is not, and a comma is never guessed at.
                if (t.Contains(',')) return null;
                return decimal.TryParse(t.Replace("_", "", StringComparison.Ordinal), NumberStyles.Float, inv, out var whole)
                       && whole == decimal.Truncate(whole)
                    ? decimal.Truncate(whole).ToString("0", inv)
                    : null;
            case ParameterValueKind.Decimal or ParameterValueKind.Double:
                if (t.Contains(',') || !t.Contains('_')) return null;
                var plain = StripMoney(t).Replace("_", "", StringComparison.Ordinal);
                return decimal.TryParse(plain, NumberStyles.Float, inv, out _) ? plain : null;
            case ParameterValueKind.Boolean:
                return BoolShorthand.GetValueOrDefault(t.ToLowerInvariant());
            case ParameterValueKind.Enum:
                // One typo from a single accepted value (two for long names); "long" is not a misspelled "buy".
                var (closest, _, unique) = OperationNames.Closest(t, p.EnumValues, maxDistance: t.Length >= 8 ? 2 : 1);
                return closest is not null && unique ? closest : null;
            case ParameterValueKind.Date:
                return RepairDate(t)?.ToString("yyyy-MM-dd", inv);
            case ParameterValueKind.Time:
                return RepairTime(t)?.ToString("HH:mm", inv);
            default:
                return null;
        }
    }

    private static DateOnly? RepairDate(string t)
    {
        var inv = CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(t, DateRepairFormats, inv, DateTimeStyles.AllowWhiteSpaces, out var date)) return date;

        // An ISO instant: the date is its first ten characters.
        if (t.Length > 10 && t[10] is 'T' or ' ' && DateOnly.TryParseExact(t[..10], "yyyy-MM-dd", inv, DateTimeStyles.None, out date))
            return date;

        // 30/10/2026 or 10/30/2026: only when one part can't be a month. 03/04/2026 is two dates.
        var m = SlashDate().Match(t);
        if (!m.Success) return null;
        int a = int.Parse(m.Groups[1].Value, inv), b = int.Parse(m.Groups[2].Value, inv), year = int.Parse(m.Groups[3].Value, inv);
        var (month, day) = a > 12 && b <= 12 ? (b, a) : b > 12 && a <= 12 ? (a, b) : a == b ? (a, b) : (0, 0);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }

    private static TimeOnly? RepairTime(string t)
    {
        var inv = CultureInfo.InvariantCulture;
        if (t.Length == 4 && t.All(char.IsAsciiDigit) && TimeOnly.TryParseExact(t, TimeDigitsFormat, inv, DateTimeStyles.None, out var time)) return time;
        return TimeOnly.TryParseExact(t, TimeMeridiemFormats, inv, DateTimeStyles.None, out time) ? time : null;
    }

    /// <summary>
    /// The corrected call built from bound values, or null when it does not bind cleanly: the JSON
    /// args are bound again (a correction that still fails is never offered), and the CLI form is
    /// kept only when its tokens bind too and survive the Hub's own command-line splitter.
    /// </summary>
    private static CallFix? BuildFix(
        OperationDescriptor descriptor, IReadOnlyDictionary<OperationParameter, object?> values, IReadOnlyList<CallChange> changes)
    {
        var args = new JsonObject();
        foreach (var parameter in descriptor.Parameters)
            if (values.TryGetValue(parameter, out var value) && value is not null) args[parameter.JsonName] = ToJson(value);
        if (!Bind(descriptor, JsonSerializer.SerializeToElement(args), withFix: false).Success) return null;

        var tail = ToArgv(descriptor, values);
        if (tail is not null && !Bind(descriptor, tail, withFix: false).Success) tail = null;
        IReadOnlyList<string>? argv = tail is null ? null : [.. descriptor.Name.Split(' '), .. tail];
        var commandLine = argv is null ? null : OperationDescriptor.ProgramName + " " + string.Join(' ', argv.Select(Quote));
        if (commandLine is not null && !SplitsBackTo(commandLine, argv!)) (commandLine, argv) = (null, null);
        return new CallFix(descriptor.Name, commandLine, argv, args, changes);
    }

    /// <summary>
    /// The corrected call for a misspelled operation name: the caller's own arguments bound to the
    /// operation that was meant (with their corrections, if they needed any), or null when they
    /// don't bind to it.
    /// </summary>
    internal static CallFix? CorrectedCall(OperationDescriptor descriptor, BindResult bound, CallChange rename) =>
        bound.Values is null ? null : BuildFix(descriptor, bound.Values, [rename, .. bound.Fix?.Changes ?? []]);

    /// <summary>The corrected call as text for a terminal or an assistant reading only the message.</summary>
    internal static string FixLines(CallFix fix) =>
        "Corrected call (not run; resend it to run it):" + Environment.NewLine
        + (fix.CommandLine is null ? "" : "  " + fix.CommandLine + Environment.NewLine)
        + "  " + fix.Operation + " " + fix.Args.ToJsonString(ReadableJson);

    // Quotes inside a value stay \" rather than ", so the line can be read and pasted.
    private static readonly JsonSerializerOptions ReadableJson =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static bool SplitsBackTo(string commandLine, IReadOnlyList<string> argv)
    {
        try { return ArgvParser.Split(commandLine).Skip(1).SequenceEqual(argv); }
        catch (FormatException) { return false; }
    }

    /// <summary>A token as <see cref="ArgvParser.Split"/> reads it back: quoted when it is empty or has a space or a quote.</summary>
    private static string Quote(string token) =>
        token.Length > 0 && !token.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')
            ? token
            : "\"" + token.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>The tokens after the operation name for these values, or null when they can't be written as tokens.</summary>
    private static List<string>? ToArgv(OperationDescriptor descriptor, IReadOnlyDictionary<OperationParameter, object?> values)
    {
        var positionals = new List<string>();
        var skipped = false;
        foreach (var argument in descriptor.Arguments)
        {
            if (!values.TryGetValue(argument, out var value) || value is null)
            {
                skipped = true;
                continue;
            }
            if (skipped) return null; // a later argument without an earlier one has no positional form
            if (value is string[] items)
            {
                if (items.Any(x => x.Length == 0 || x.Contains(','))) return null;
                positionals.AddRange(items);
            }
            else positionals.Add(ToText(value));
        }

        var options = new List<string>();
        foreach (var option in descriptor.Options)
        {
            if (!values.TryGetValue(option, out var value) || value is null) continue;
            switch (value)
            {
                case bool flag:
                    options.Add(flag ? "--" + option.Name : "--no-" + option.Name);
                    break;
                case string[] items:
                    if (items.Any(x => x.Length == 0 || x.Contains(',') || ArgvParser.IsLongOptionToken(x))) return null;
                    foreach (var item in items) options.AddRange(["--" + option.Name, item]);
                    break;
                default:
                    var text = ToText(value);
                    if (ArgvParser.IsLongOptionToken(text)) options.Add($"--{option.Name}={text}");
                    else options.AddRange(["--" + option.Name, text]);
                    break;
            }
        }

        // A positional that looks like an option (-x, --name) has to follow the `--` end marker.
        return positionals.Any(ArgvParser.IsOptionToken)
            ? [.. options, "--", .. positionals]
            : [.. positionals, .. options];
    }

    private static string ToText(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        Guid g => g.ToString("D"),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString(t.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture),
        Enum e => OperationParameter.EnumWireName(e),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    internal static JsonNode? ToJson(object value) => value switch
    {
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        decimal m => JsonValue.Create(m),
        double d => JsonValue.Create(d),
        string[] items => new JsonArray([.. items.Select(x => (JsonNode?)JsonValue.Create(x))]),
        _ => JsonValue.Create(ToText(value)),
    };

    private static OperationParameter? FindOption(OperationDescriptor descriptor, string name) =>
        descriptor.Options.FirstOrDefault(p => p.Name == name)
        ?? descriptor.Options.FirstOrDefault(p => OperationNames.Fold(p.Name) == OperationNames.Fold(name));

    private static OperationParameter? FindByJsonKey(OperationDescriptor descriptor, string key)
    {
        var folded = OperationNames.Fold(key);
        return descriptor.Parameters.FirstOrDefault(p =>
            OperationNames.Fold(p.Name) == folded || OperationNames.Fold(p.Property.Name) == folded);
    }

    private static string UnknownMessage(string spelled, IEnumerable<string> known, string typed)
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

    /// <summary>A declared example (CLI spelling) as the value the binder reads from it, or null when the binder would refuse it.</summary>
    internal static object? ExampleValue(OperationParameter parameter, string text) =>
        ConvertText(parameter, text, out var value, out _, quoteJson: false) ? value : null;

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
                ok = DateOnly.TryParseExact(text, DateFormats, inv, DateTimeStyles.None, out var date);
                value = date;
                break;
            case ParameterValueKind.Time:
                ok = TimeOnly.TryParseExact(text.Trim(), TimeFormats, inv, DateTimeStyles.None, out var time);
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
            error = $"{label} expects {Expectation(p)}, got '{text}'." +
                    (p.ValueKind is ParameterValueKind.Decimal or ParameterValueKind.Double && text.Contains(',')
                        ? " Commas are ambiguous (1,005 vs 1.005): write 1005, 1.005 or 1,005.00."
                        : "");
        }
        return ok;
    }

    private static string Expectation(OperationParameter p) => p.ValueKind == ParameterValueKind.Enum
        ? "one of " + string.Join(", ", p.EnumValues)
        : ValueFormats.For(p.ValueKind).Expects;

    private static bool TryBool(string text, out bool value)
    {
        var word = text.Trim().ToLowerInvariant();
        value = TrueWords.Contains(word);
        return value || FalseWords.Contains(word);
    }

    /// <summary>Lets <c>$1,250.50</c> through as a number (people paste prices).</summary>
    /// <summary>
    /// No comma at all, or commas only between groups of exactly three digits before any decimal point,
    /// with a leading group that does not start with 0: "0,001" is a comma decimal (0.001), never a
    /// thousands group, and reading it as 1 made a 1000x order or withdrawal.
    /// </summary>
    private static bool ThousandsOk(string text) =>
        // ...and only with a decimal point too (IP-T511): "1,005" is 1005 to some and 1.005 to others,
        // so a bare comma group is refused; "1,005.00", "1005" and "1.005" are unambiguous.
        !text.Contains(',') || Regex.IsMatch(text, ThousandsPattern);

    private static string StripMoney(string text) => text.Trim().Replace("$", "", StringComparison.Ordinal);

    [GeneratedRegex(@"^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$")]
    private static partial Regex SlashDate();
}
