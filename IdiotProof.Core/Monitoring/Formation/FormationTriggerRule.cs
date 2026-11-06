using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Core.Catalogs;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// What a live snapshot must clear to count as an arm-worthy gapper, from
/// <c>formation-trigger.json</c> (IP-LAW-7). Every threshold is nullable and the shipped file is
/// all null, so the rule never passes until the study sets it. A set threshold whose feature is
/// null in the snapshot fails (closed).
/// </summary>
public sealed record FormationTriggerRule(
    double? MinGapPct = null,
    double? MaxGapPct = null,
    double? MinVolRatioTod = null,
    double? MinVelocity3m = null,
    double? MinAccel = null,
    double? MaxDistFromPmHighPct = null,
    double? MinCatalystScore = null,
    bool? RequireCatalyst = null)
{
    public const string FileName = "formation-trigger.json";

    public static readonly FormationTriggerRule Never = new();

    public bool IsSet =>
        MinGapPct is not null || MaxGapPct is not null || MinVolRatioTod is not null || MinVelocity3m is not null
        || MinAccel is not null || MaxDistFromPmHighPct is not null || MinCatalystScore is not null || RequireCatalyst is true;

    public bool Passes(GapperFormationSnapshot s)
    {
        if (!IsSet) return false;
        if (MinGapPct is { } minGap && !(s.GapPct >= minGap)) return false;
        if (MaxGapPct is { } maxGap && !(s.GapPct <= maxGap)) return false;
        if (MinVolRatioTod is { } minVol && !(s.VolRatioTod >= minVol)) return false;
        if (MinVelocity3m is { } minV3 && !(s.Velocity3m >= minV3)) return false;
        if (MinAccel is { } minAccel && !(s.Acceleration >= minAccel)) return false;
        if (MaxDistFromPmHighPct is { } maxDist && !(s.DistToPmHighPct <= maxDist)) return false;
        if (MinCatalystScore is { } minScore && !(s.CatalystScore >= minScore)) return false;
        if (RequireCatalyst is true && s.CatalystScore is null) return false;
        return true;
    }

    public string Describe()
    {
        if (!IsSet) return "never (every threshold is null)";
        var sb = new StringBuilder();
        void Add(string label, double? v, string unit = "") { if (v is { } x) sb.Append(sb.Length > 0 ? ", " : "").Append(label).Append(' ').Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(unit); }
        Add("gap >=", MinGapPct, "%");
        Add("gap <=", MaxGapPct, "%");
        Add("volRatioTod >=", MinVolRatioTod, "x");
        Add("v3 >=", MinVelocity3m, "%/min");
        Add("accel >=", MinAccel);
        Add("distPmHigh <=", MaxDistFromPmHighPct, "%");
        Add("catalyst >=", MinCatalystScore);
        if (RequireCatalyst is true) sb.Append(sb.Length > 0 ? ", " : "").Append("catalyst required");
        return sb.ToString();
    }

    public static FormationTriggerRule Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<Document>(json, Options)
            ?? throw new InvalidDataException("formation-trigger.json is empty.");
        return new FormationTriggerRule(doc.MinGapPct, doc.MaxGapPct, doc.MinVolRatioTod, doc.MinVelocity3m, doc.MinAccel,
            doc.MaxDistFromPmHighPct, doc.MinCatalystScore, doc.RequireCatalyst);
    }

    public static FormationTriggerRule Load(IStaticCatalogSource? source = null) =>
        Parse(File.ReadAllText((source ?? StaticCatalogSource.Default).Resolve(FileName)));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class Document
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("minGapPct")] public double? MinGapPct { get; set; }
        [JsonPropertyName("maxGapPct")] public double? MaxGapPct { get; set; }
        [JsonPropertyName("minVolRatioTod")] public double? MinVolRatioTod { get; set; }
        [JsonPropertyName("minVelocity3m")] public double? MinVelocity3m { get; set; }
        [JsonPropertyName("minAccel")] public double? MinAccel { get; set; }
        [JsonPropertyName("maxDistFromPmHighPct")] public double? MaxDistFromPmHighPct { get; set; }
        [JsonPropertyName("minCatalystScore")] public double? MinCatalystScore { get; set; }
        [JsonPropertyName("requireCatalyst")] public bool? RequireCatalyst { get; set; }
    }
}
