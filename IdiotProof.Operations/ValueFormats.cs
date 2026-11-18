using System.Text.Json.Nodes;

namespace IdiotProof.Operations;

/// <summary>
/// How one kind of argument value may be written (IP-T526): what the binder reads
/// (<see cref="Accepts"/>), what it refuses but can correct to a single reading
/// (<see cref="Corrected"/>, returned as a <c>value</c> change), what it refuses with no correction
/// (<see cref="NeverCorrected"/>), and one accepted value (<see cref="Example"/>, as JSON).
/// </summary>
public sealed record ValueFormat(
    ParameterValueKind Kind,
    string Type,
    string Expects,
    string Json,
    IReadOnlyList<string> Accepts,
    IReadOnlyList<string> Corrected,
    IReadOnlyList<string> NeverCorrected,
    JsonNode? Example)
{
    public JsonObject ToJson() => new()
    {
        ["type"] = Type,
        ["expects"] = Expects,
        ["json"] = Json,
        ["accepts"] = new JsonArray([.. Accepts.Select(a => (JsonNode)a)]),
        ["corrected"] = new JsonArray([.. Corrected.Select(a => (JsonNode)a)]),
        ["never_corrected"] = new JsonArray([.. NeverCorrected.Select(a => (JsonNode)a)]),
        ["example"] = Example?.DeepClone(),
    };
}

/// <summary>
/// The accepted forms of every argument type, built from the tables <see cref="ArgsBinder"/>
/// converts with (its date, time and flag lists and its thousands pattern), so the agent catalog
/// publishes what the binder does rather than a second description of it (IP-LAW-4, IP-T526).
/// </summary>
public static class ValueFormats
{
    private static readonly Dictionary<ParameterValueKind, ValueFormat> ByKind = Build().ToDictionary(f => f.Kind);

    /// <summary>One entry per <see cref="ParameterValueKind"/>, in enum order.</summary>
    public static IReadOnlyList<ValueFormat> All { get; } = [.. Enum.GetValues<ParameterValueKind>().Select(k => ByKind[k])];

    public static ValueFormat For(ParameterValueKind kind) => ByKind[kind];

    /// <summary>The table as the catalog publishes it, keyed by type label (<c>date</c>, <c>decimal</c>, <c>enum</c>, ...).</summary>
    public static JsonObject ToJson()
    {
        var json = new JsonObject();
        foreach (var format in All) json[format.Type] = format.ToJson();
        return json;
    }

    private static IEnumerable<ValueFormat> Build()
    {
        const string wholeJson = "A JSON number without a fraction, or a string in an accepted form.";
        const string numberJson = "A JSON number, or a string in an accepted form.";
        string[] wholeAccepts = ["digits with an optional sign: 5, -3"];
        string[] wholeCorrected = ["a whole number with a zero fraction, an exponent or underscores: 5.0, 1e3, 1_000"];
        string[] wholeNever = ["a fraction: 1.5", "any comma: 1,000", "a word: five"];
        string[] numberAccepts =
        [
            "digits with an optional sign, decimal point and exponent: 12.5, -0.25, 1e3",
            "a leading $ is ignored: $12.50",
            $"commas only as thousands groups together with a decimal point ({ArgsBinder.ThousandsPattern}): 1,250.50",
        ];
        string[] numberCorrected = ["underscores as digit separators: 1_250.5"];
        string[] numberNever = ["a comma without a decimal point (1,005 is 1005 to some and 1.005 to others)", "a comma decimal: 0,25", "a word: ten"];

        yield return new(ParameterValueKind.String, "string", "text",
            "A JSON string; a number or a boolean is taken as its text.",
            ["any text"], [], [], "text");
        yield return new(ParameterValueKind.Boolean, "bool", "true or false",
            "A JSON boolean, or a string in an accepted form.",
            [
                .. ArgsBinder.TrueWords.Concat(ArgsBinder.FalseWords),
                "any letter case",
                "on the CLI: --name, --name=false, --no-name",
            ],
            [.. ArgsBinder.BoolShorthand.Select(kv => $"{kv.Key} (becomes {kv.Value})")],
            ["any other word"], true);
        yield return new(ParameterValueKind.Int32, "int", "a whole number", wholeJson,
            [.. wholeAccepts, $"from {int.MinValue} to {int.MaxValue}"], wholeCorrected, wholeNever, 5);
        yield return new(ParameterValueKind.Int64, "long", "a whole number", wholeJson,
            [.. wholeAccepts, $"from {long.MinValue} to {long.MaxValue}"], wholeCorrected, wholeNever, 5);
        yield return new(ParameterValueKind.Decimal, "decimal", "a number", numberJson,
            numberAccepts, numberCorrected, numberNever, 12.5m);
        yield return new(ParameterValueKind.Double, "number", "a number", numberJson,
            [.. numberAccepts, "finite only"], numberCorrected, numberNever, 12.5);
        yield return new(ParameterValueKind.Guid, "uuid", "a GUID",
            "A JSON string.",
            ["32 hex digits, with or without hyphens, braces or parentheses: 0f8fad5b-d9cb-469f-a165-70867728950e"],
            [], ["anything else"], "0f8fad5b-d9cb-469f-a165-70867728950e");
        yield return new(ParameterValueKind.Date, "date", $"a date ({ArgsBinder.DateFormats[0]})",
            "A JSON string.",
            ArgsBinder.DateFormats,
            [
                .. ArgsBinder.DateRepairFormats,
                "an ISO instant (its date part): 2026-10-30T09:30:00",
                "d/M/yyyy or M/d/yyyy (with / or -) when only one part can be the month: 30/10/2026",
            ],
            ["a slash date that reads two ways: 03/04/2026", "a two-digit year", "a word: today"],
            "2026-10-30");
        yield return new(ParameterValueKind.Time, "time", "an ET time of day (HH:mm, e.g. 09:30 or 15:55)",
            "A JSON string. Always US Eastern time.",
            ArgsBinder.TimeFormats,
            [$"{ArgsBinder.TimeDigitsFormat} as four digits: 0930", .. ArgsBinder.TimeMeridiemFormats.Select(f => $"{f}: {(f.Contains(' ') ? "9 am" : "9am")}")],
            ["a time zone or offset", "a word: open"],
            "09:30");
        yield return new(ParameterValueKind.Enum, "enum", "one of the parameter's enum_values",
            "A JSON string.",
            ["one of the parameter's enum_values; letter case, hyphens and underscores are ignored"],
            ["one typo from exactly one accepted value (two typos when 8 or more letters were sent)"],
            ["a synonym (long is not buy)", "a value equally close to two accepted values"],
            null);
        yield return new(ParameterValueKind.StringArray, "string[]", "a list of strings",
            "A JSON array of strings, or one string split on commas.",
            ["on the CLI: a comma list (A,B), or the option repeated; a trailing positional list takes every remaining token"],
            [], ["a JSON array holding anything but strings"],
            new JsonArray("A", "B"));
    }
}
