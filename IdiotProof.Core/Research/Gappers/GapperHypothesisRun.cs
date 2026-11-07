using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Evidence.Search;

namespace IdiotProof.Core.Research.Gappers;

/// <summary>The four verdicts a hypothesis test can return; nothing else is ever written.</summary>
public static class HypothesisVerdict
{
    public const string Supported = "supported";
    public const string Refuted = "refuted";
    public const string Undecided = "undecided";
    public const string Void = "void";

    /// <summary>Combines a hypothesis's parts: every part must hold for support; one refuted part refutes; one void part voids.</summary>
    public static string Combine(IEnumerable<string> parts)
    {
        var list = parts.ToList();
        if (list.Count == 0) return Void;
        if (list.Any(v => v == Void)) return Void;
        if (list.Any(v => v == Refuted)) return Refuted;
        if (list.All(v => v == Supported)) return Supported;
        return Undecided;
    }
}

/// <summary>Which side of the minimum effect the claim lives on: the effect must be at least it, or at most it.</summary>
public enum EffectDirection { AtLeast, AtMost }

/// <summary>
/// One testable part of a hypothesis (most have one; H5, H7 and H10 have two): its effect against
/// the frozen minimum, its raw and corrected p where the family uses one, its interval, and its
/// verdict. The run file is a list of these under the hypothesis header.
/// </summary>
public sealed class HypothesisPart
{
    public string Name { get; set; } = "";
    public string EffectName { get; set; } = "";
    public double? Effect { get; set; }
    public double? MinEffect { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public EffectDirection Direction { get; set; }
    public bool? MeetsMinimum { get; set; }
    public double? RawP { get; set; }
    public double? FamilyQ { get; set; }
    public double? HolmP { get; set; }
    public double? CiLow { get; set; }
    public double? CiHigh { get; set; }
    public int N { get; set; }
    public int Days { get; set; }
    public string Verdict { get; set; } = HypothesisVerdict.Undecided;

    /// <summary>True when the test fixed the verdict itself (a range interval, a void), so the correction pass leaves it.</summary>
    public bool VerdictFixed { get; set; }

    public List<string> Reasons { get; set; } = [];
    public Dictionary<string, object?> Statistic { get; set; } = new(StringComparer.Ordinal);

    public bool HasInterval => CiLow is { } lo && CiHigh is { } hi && !double.IsNaN(lo) && !double.IsNaN(hi);

    /// <summary>The interval sits entirely where the claim says it should.</summary>
    public bool IntervalMeets() => HasInterval && MinEffect is { } m && (Direction == EffectDirection.AtLeast ? CiLow >= m : CiHigh <= m);

    /// <summary>The interval sits entirely where the claim cannot hold.</summary>
    public bool IntervalExcludes() => HasInterval && MinEffect is { } m && (Direction == EffectDirection.AtLeast ? CiHigh < m : CiLow > m);

    public static HypothesisPart VoidPart(string name, string reason) => new() { Name = name, Verdict = HypothesisVerdict.Void, VerdictFixed = true, Reasons = [reason] };
}

public sealed class HypothesisRunInputs
{
    public int N { get; set; }
    public int Days { get; set; }
    public string? FirstDate { get; set; }
    public string? LastDate { get; set; }
    public string Population { get; set; } = "";
    public List<string> FeatureColumns { get; set; } = [];
    public List<string> LabelColumns { get; set; } = [];
    public string? FrozenCommit { get; set; }
    public string? Head { get; set; }
    public string HypothesesSha256 { get; set; } = "";
    public string ParamsSha256 { get; set; } = "";
    public List<string> PostRegistrationFeaturesUsed { get; set; } = [];

    /// <summary>Set only on an amended hypothesis's run (amendment 01's id), so frozen and amended runs are corrected as separate sets.</summary>
    public string? AmendmentId { get; set; }
    public string? AmendmentSha256 { get; set; }
    public string? AmendsHypothesis { get; set; }
}

/// <summary>One hypothesis on one split, as written to <c>runs/test-&lt;H&gt;-&lt;split&gt;-&lt;stamp&gt;.json</c>.</summary>
public sealed class HypothesisRun
{
    public string Id { get; set; } = "";
    public string Family { get; set; } = "";
    public bool Primary { get; set; }
    public string Test { get; set; } = "";
    public string Claim { get; set; } = "";
    public string? Feature { get; set; }
    public string? Label { get; set; }
    public string? DecisionTime { get; set; }
    public Dictionary<string, JsonElement> MinEffect { get; set; } = new();
    public string Split { get; set; } = "";
    public bool Smoke { get; set; }
    public HypothesisRunInputs Inputs { get; set; } = new();
    public List<HypothesisPart> Parts { get; set; } = [];
    public string Verdict { get; set; } = HypothesisVerdict.Undecided;
    public List<string> Reasons { get; set; } = [];
    public string CorrectionScope { get; set; } = "";
    public DateTime ComputedUtc { get; set; }
    public double Seconds { get; set; }
    public string? File { get; set; }

    /// <summary>The p Holm sees for this hypothesis: the largest raw p across its parts (every part must hold).</summary>
    public double? HolmInputP => Parts.Where(p => p.RawP is { } v && !double.IsNaN(v)).Select(p => p.RawP).DefaultIfEmpty(null).Max();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static HypothesisRun? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<HypothesisRun>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static string FileName(string id, string split, DateTime stampUtc) =>
        $"test-{id}-{split}-{stampUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.json";
}

/// <summary>
/// The protocol's corrections applied to a set of runs: Benjamini-Hochberg within each family over
/// every part that carries a p, Holm step-down across the primaries that yield a p (the list in
/// params), then the verdict of every part and hypothesis. F3 parts never carry a p and are judged by
/// their intervals alone; H12 is judged by SPA and DSR (its q is informational).
/// </summary>
public static class HypothesisCorrections
{
    public const string SpaKey = "spaP";
    public const string DsrKey = "dsr";

    /// <summary>
    /// Corrects frozen runs exactly as <see cref="Apply"/> always has, and amended runs as a set of their
    /// own (BH within family among amended runs, Holm across the amended primaries whose frozen
    /// hypothesis is in the Holm list), so an amendment never changes a frozen q or Holm p.
    /// </summary>
    public static void ApplyAll(IReadOnlyList<HypothesisRun> runs, GapperStudyParams p, string scope)
    {
        Apply(runs.Where(r => r.Inputs.AmendmentId is null).ToList(), p, scope);
        foreach (var amendment in runs.Where(r => r.Inputs.AmendmentId is not null).GroupBy(r => r.Inputs.AmendmentId!))
        {
            var amended = amendment.ToList();
            var holmIds = amended.Where(r => r.Inputs.AmendsHypothesis is { } b && p.Statistics.HolmOverPrimariesWithPValue.Contains(b, StringComparer.OrdinalIgnoreCase)).Select(r => r.Id).ToList();
            Apply(amended, p, scope + $"; amendment {amendment.Key} corrected as its own set", holmIds);
        }
    }

    /// <summary>A strategy grid judged by its minimum effects, SPA and DSR: the frozen H12, its amended form, and the news-ride grid.</summary>
    public static bool IsStrategyGrid(HypothesisRun run) =>
        run.Id.Equals("H12", StringComparison.OrdinalIgnoreCase)
        || run.Inputs.AmendsHypothesis?.Equals("H12", StringComparison.OrdinalIgnoreCase) == true
        || run.Test.Equals(NewsRideGridTest, StringComparison.Ordinal);

    public const string NewsRideGridTest = "news-ride-grid";

    public static void Apply(IReadOnlyList<HypothesisRun> runs, GapperStudyParams p, string scope, IReadOnlyCollection<string>? holmIds = null)
    {
        holmIds ??= p.Statistics.HolmOverPrimariesWithPValue;
        foreach (var family in runs.GroupBy(r => r.Family))
        {
            var parts = family.SelectMany(r => r.Parts).Where(x => x.RawP is { } v && !double.IsNaN(v)).ToList();
            var q = MultipleTesting.BenjaminiHochberg(parts.Select(x => x.RawP!.Value).ToList());
            for (int i = 0; i < parts.Count; i++) parts[i].FamilyQ = q[i];
        }
        var holmSet = runs.Where(r => r.Primary && holmIds.Contains(r.Id, StringComparer.OrdinalIgnoreCase) && r.HolmInputP is { } hp && !double.IsNaN(hp)).ToList();
        var holm = MultipleTesting.Holm(holmSet.Select(r => r.HolmInputP!.Value).ToList());
        for (int i = 0; i < holmSet.Count; i++)
            foreach (var part in holmSet[i].Parts) part.HolmP = holm[i];

        foreach (var run in runs)
        {
            run.CorrectionScope = scope;
            foreach (var part in run.Parts) Decide(run, part, p);
            run.Verdict = HypothesisVerdict.Combine(run.Parts.Select(x => x.Verdict));
        }
    }

    private static void Decide(HypothesisRun run, HypothesisPart part, GapperStudyParams p)
    {
        if (part.Verdict == HypothesisVerdict.Void || part.VerdictFixed) return;
        part.Reasons.RemoveAll(r => r.StartsWith("verdict:", StringComparison.Ordinal));
        if (run.Family == "F3")
        {
            part.Verdict = part.IntervalMeets() ? HypothesisVerdict.Supported : part.IntervalExcludes() ? HypothesisVerdict.Refuted : HypothesisVerdict.Undecided;
            part.Reasons.Add($"verdict: interval [{Fmt(part.CiLow)}, {Fmt(part.CiHigh)}] against {part.MinEffect} ({part.Direction}); descriptive, no p");
            return;
        }
        if (IsStrategyGrid(run))
        {
            double? spa = part.Statistic.TryGetValue(SpaKey, out var sp) && sp is double d1 ? d1 : null;
            double? dsr = part.Statistic.TryGetValue(DsrKey, out var ds) && ds is double d2 ? d2 : null;
            bool corrected = spa is { } s && s < p.Statistics.SpaAlpha && dsr is { } d && d >= p.Statistics.DsrMin;
            part.Verdict = part.MeetsMinimum == true && corrected ? HypothesisVerdict.Supported : part.IntervalExcludes() ? HypothesisVerdict.Refuted : HypothesisVerdict.Undecided;
            part.Reasons.Add($"verdict: SPA p {Fmt(spa)} vs {p.Statistics.SpaAlpha}, DSR {Fmt(dsr)} vs {p.Statistics.DsrMin}, worth-trading {part.MeetsMinimum}");
            return;
        }
        bool bh = part.FamilyQ is { } q && q < p.Statistics.BhQ;
        bool holm = !run.Primary || part.HolmP is null || part.HolmP < p.Statistics.HolmAlpha;
        if (part.MeetsMinimum == true && bh && holm) part.Verdict = HypothesisVerdict.Supported;
        else if (part.IntervalExcludes()) part.Verdict = HypothesisVerdict.Refuted;
        else part.Verdict = HypothesisVerdict.Undecided;
        part.Reasons.Add($"verdict: effect {Fmt(part.Effect)} vs {part.MinEffect} ({part.Direction}), q {Fmt(part.FamilyQ)} vs {p.Statistics.BhQ}" +
            (run.Primary && part.HolmP is not null ? $", Holm p {Fmt(part.HolmP)} vs {p.Statistics.HolmAlpha}" : ""));
    }

    public static string Fmt(double? v) => v is { } x && !double.IsNaN(x) ? x.ToString(Math.Abs(x) < 0.001 && x != 0 ? "0.0e0" : "0.000", CultureInfo.InvariantCulture) : "n/a";
}
