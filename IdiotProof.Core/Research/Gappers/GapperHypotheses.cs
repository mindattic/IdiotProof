using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>One pre-registered hypothesis as <c>hypotheses.json</c> declares it.</summary>
public sealed class GapperHypothesis
{
    public string Id { get; init; } = "";
    public bool Primary { get; init; }
    public string Family { get; init; } = "";
    public string Claim { get; init; } = "";
    public string? Feature { get; init; }
    public string? Label { get; init; }
    public string? DecisionTime { get; init; }
    public string Test { get; init; } = "";
    public Dictionary<string, JsonElement> MinEffect { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SplitsAllowed { get; init; } = [];
    public string? Notes { get; init; }

    public double MinEffectNumber(string key) =>
        MinEffect.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble()
        : throw new InvalidDataException($"{Id}: minEffect.{key} is missing or not a number.");

    public double[] MinEffectRange(string key) =>
        MinEffect.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetDouble()).ToArray()
        : throw new InvalidDataException($"{Id}: minEffect.{key} is missing or not an array.");

    public bool AllowsSplit(string split) => SplitsAllowed.Contains(split, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The frozen hypothesis set with its header.</summary>
public sealed class GapperHypothesisSet
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public int Version { get; init; }
    public DateTime? FrozenUtc { get; init; }
    public string? Notes { get; init; }
    public List<GapperHypothesis> Hypotheses { get; init; } = [];

    [JsonIgnore] public string Sha256 { get; private set; } = "";

    public static GapperHypothesisSet Load(GapperStudyPaths paths) => Parse(File.ReadAllText(paths.HypothesesFile));

    public static GapperHypothesisSet Parse(string json)
    {
        var set = JsonSerializer.Deserialize<GapperHypothesisSet>(json, Options) ?? throw new InvalidDataException("hypotheses.json is empty.");
        set.Sha256 = GapperStudyParams.Sha256(json);
        return set;
    }

    public GapperHypothesis Find(string id) =>
        Hypotheses.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"hypotheses.json declares no hypothesis '{id}'.");

    public IReadOnlyList<GapperHypothesis> Family(string family) => Hypotheses.Where(h => h.Family == family).ToList();
}

/// <summary>One clause of a hypothesis feature condition: a snapshot column compared with a number.</summary>
public sealed record FeatureClause(string Name, string Operator, double Threshold)
{
    public bool Holds(double? value) => value is { } v && Operator switch
    {
        ">" => v > Threshold,
        ">=" => v >= Threshold,
        "<" => v < Threshold,
        "<=" => v <= Threshold,
        "==" or "=" => v == Threshold,
        _ => false,
    };
}

/// <summary>
/// Parses the declared feature text of a hypothesis ("v_3 > 1.0 AND pmVolRatio > 5",
/// "daysSinceReverseSplit &lt;= 30") into clauses, so the thresholds the tests apply are read from
/// <c>hypotheses.json</c> and never retyped.
/// </summary>
public static partial class FeatureCondition
{
    public static IReadOnlyList<FeatureClause> Parse(string? feature)
    {
        if (string.IsNullOrWhiteSpace(feature)) return [];
        var clauses = new List<FeatureClause>();
        foreach (Match m in ClausePattern().Matches(feature))
            clauses.Add(new FeatureClause(m.Groups["name"].Value, m.Groups["op"].Value, double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture)));
        return clauses;
    }

    /// <summary>The first "HH:mm" clock time named in a hypothesis text, or null.</summary>
    public static TimeSpan? Time(string? text) =>
        text is not null && TimePattern().Match(text) is { Success: true } m ? GapperStudyParams.ParseEt(m.Groups[1].Value) : null;

    [GeneratedRegex(@"\b(\d{2}:\d{2})\b")]
    private static partial Regex TimePattern();

    /// <summary>The single numeric threshold of a one-clause condition on <paramref name="name"/>.</summary>
    public static FeatureClause Require(string? feature, string name)
    {
        var clause = Parse(feature).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return clause ?? throw new InvalidDataException($"The feature text '{feature}' declares no numeric condition on {name}.");
    }

    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_:\.]*)\s*(?<op>>=|<=|==|=|>|<)\s*(?<value>-?\d+(\.\d+)?)(?![\d:])")]
    private static partial Regex ClausePattern();
}
