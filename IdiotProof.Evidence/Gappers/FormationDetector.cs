using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Evidence.Evaluation;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// The 28 values the gapper study's detector reads from one formation snapshot (H11 / H11p, IP-T442). The study's
/// panel rows and the live recorder's snapshots both map into this record, and <see cref="FormationDetector.Row"/>
/// turns it into the model's input, so the transform the model was fit on is the one it scores live with.
/// </summary>
public sealed record FormationDetectorValues(
    double? GapPct, double PmCumVol, double? VolRatioTod, double? OwnProfileRatio,
    double? Velocity1m, double? Velocity3m, double? Velocity5m, double? Velocity15m, double? Acceleration, double? VolumeRoc,
    double TradesProxy, double? TradesRoc, double? SpreadProxy, double? DistToPmHighPct, double? MinsSinceFirstAbnormal,
    double? AbnormalReturnD1, double? AbnormalReturnD2, double? AbnormalReturnD3, double? AbnormalReturnD4, double? AbnormalReturnD5,
    double? VolumeRatioD1, double? VolumeRatioD2, double? VolumeRatioD3, double? VolumeRatioD4, double? VolumeRatioD5,
    double? DaysSinceReverseSplit, double? DaysSince424B, double? DaysSinceS1);

/// <summary>
/// The gapper study's validated detector (H11p, IP-T442 amendment 01): a logistic model on the 08:00 ET snapshot for
/// "reaches +20% over the prior close by 09:45", fit on the train split only, which beat the catalyst-latency
/// benchmark on validation (weighted AUC 0.912 vs 0.558). It ranks; it is not a trade: every trading rule built on
/// these snapshots lost money on validation (H12, H12p, H18). IP-T447 uses it as a watch-only alert.
/// </summary>
public static class FormationDetector
{
    /// <summary>The ET decision time the detector was fit and validated at; it is not valid at any other time.</summary>
    public static readonly TimeSpan DecisionTimeEt = new(8, 0, 0);

    public static readonly IReadOnlyList<string> Features =
    [
        "GapPct", "LogPmCumVol", "VolRatioTod", "OwnProfileRatio", "Velocity1m", "Velocity3m", "Velocity5m", "Velocity15m", "Acceleration", "VolumeRoc",
        "TradesProxy", "TradesRoc", "SpreadProxy", "DistToPmHighPct", "MinsSinceFirstAbnormal",
        "AbnormalReturnD1", "AbnormalReturnD2", "AbnormalReturnD3", "AbnormalReturnD4", "AbnormalReturnD5",
        "VolumeRatioD1", "VolumeRatioD2", "VolumeRatioD3", "VolumeRatioD4", "VolumeRatioD5",
        "DaysSinceReverseSplit", "DaysSince424B", "DaysSinceS1",
    ];

    /// <summary>The model input: missing values as NaN (scored as the training mean), cumulative volume as log(1 + v).</summary>
    public static double[] Row(FormationDetectorValues v)
    {
        static double N(double? x) => x ?? double.NaN;
        return
        [
            N(v.GapPct), Math.Log(1 + Math.Max(0, v.PmCumVol)), N(v.VolRatioTod), N(v.OwnProfileRatio), N(v.Velocity1m), N(v.Velocity3m), N(v.Velocity5m), N(v.Velocity15m), N(v.Acceleration), N(v.VolumeRoc),
            v.TradesProxy, N(v.TradesRoc), N(v.SpreadProxy), N(v.DistToPmHighPct), N(v.MinsSinceFirstAbnormal),
            N(v.AbnormalReturnD1), N(v.AbnormalReturnD2), N(v.AbnormalReturnD3), N(v.AbnormalReturnD4), N(v.AbnormalReturnD5),
            N(v.VolumeRatioD1), N(v.VolumeRatioD2), N(v.VolumeRatioD3), N(v.VolumeRatioD4), N(v.VolumeRatioD5),
            N(v.DaysSinceReverseSplit), N(v.DaysSince424B), N(v.DaysSinceS1),
        ];
    }

    /// <summary>The values of a live (or recomputed) snapshot; TradesRoc lives in its extras.</summary>
    public static FormationDetectorValues From(GapperFormationSnapshot s) => new(
        s.GapPct, s.PmCumVol, s.VolRatioTod, s.OwnProfileRatio, s.Velocity1m, s.Velocity3m, s.Velocity5m, s.Velocity15m, s.Acceleration, s.VolumeRoc,
        s.TradesProxy, s.Extra.TryGetValue(GapperExtraKeys.TradesRoc, out var roc) ? roc : null, s.SpreadProxy, s.DistToPmHighPct, s.MinsSinceFirstAbnormal,
        s.AbnormalReturnD1, s.AbnormalReturnD2, s.AbnormalReturnD3, s.AbnormalReturnD4, s.AbnormalReturnD5,
        s.VolumeRatioD1, s.VolumeRatioD2, s.VolumeRatioD3, s.VolumeRatioD4, s.VolumeRatioD5,
        s.DaysSinceReverseSplit, s.DaysSince424B, s.DaysSinceS1);

    /// <summary>Which of the 28 inputs are missing (scored as the training mean), by feature name.</summary>
    public static IReadOnlyList<string> Missing(FormationDetectorValues v)
    {
        var row = Row(v);
        return Features.Where((_, i) => double.IsNaN(row[i]) || double.IsInfinity(row[i])).ToList();
    }
}

/// <summary>
/// The frozen detector as shipped in <c>formation-detector.json</c> (IP-T447): the fitted model's standardization and
/// weights plus where it came from, so the live score is reproducible and a reader can see what was validated.
/// </summary>
public sealed record FormationDetectorModel
{
    public const string FileName = "formation-detector.json";

    [JsonPropertyName("_comment")] public string? Comment { get; init; }
    public int Version { get; init; } = 1;
    public string Hypothesis { get; init; } = "H11p";
    public string Label { get; init; } = "reach20By0945";
    public string DecisionTimeEt { get; init; } = "08:00";
    public string FitOn { get; init; } = "train";
    public int TrainRows { get; init; }
    public double? ValidationAuc { get; init; }
    public double? ValidationBenchmarkAuc { get; init; }
    public double? TrainPositiveRate { get; init; }
    public DateTime FittedUtc { get; init; }
    public string? HarnessCommit { get; init; }
    public required IReadOnlyList<string> Features { get; init; }
    public required IReadOnlyList<double> Means { get; init; }
    public required IReadOnlyList<double> Stds { get; init; }
    public required IReadOnlyList<double> Weights { get; init; }
    public double Bias { get; init; }

    /// <summary>Per feature, the weighted share of train rows where it was missing; the live shadow flags inputs missing far more often live.</summary>
    public IReadOnlyList<double>? TrainMissingShare { get; init; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static FormationDetectorModel From(LogisticModel m) => new()
    {
        Features = m.Features.ToList(), Means = m.Means.ToList(), Stds = m.Stds.ToList(), Weights = m.Weights.ToList(), Bias = m.Bias,
    };

    /// <summary>The scoring model; throws when the file's feature list is not the detector's (a stale or foreign file).</summary>
    public LogisticModel ToModel()
    {
        if (!Features.SequenceEqual(FormationDetector.Features))
            throw new InvalidOperationException($"{FileName} lists features [{string.Join(", ", Features)}], not the detector's {FormationDetector.Features.Count}.");
        if (Means.Count != Features.Count || Stds.Count != Features.Count || Weights.Count != Features.Count)
            throw new InvalidOperationException($"{FileName} has {Means.Count} means, {Stds.Count} stds and {Weights.Count} weights for {Features.Count} features.");
        return LogisticModel.Create(Features, Means, Stds, Weights, Bias);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static FormationDetectorModel Parse(string json) =>
        JsonSerializer.Deserialize<FormationDetectorModel>(json, Json) ?? throw new InvalidOperationException($"{FileName} is empty.");
}
