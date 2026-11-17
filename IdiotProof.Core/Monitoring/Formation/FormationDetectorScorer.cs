using IdiotProof.Core.Catalogs;
using IdiotProof.Evidence.Evaluation;
using IdiotProof.Evidence.Gappers;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// The frozen gapper-study detector (H11p) for the live recorder (IP-T447), loaded once from
/// <c>formation-detector.json</c>. A missing or mismatched file leaves it off (logged), and the recorder records
/// snapshots as before without scores or alerts. Watch-only: nothing here arms or orders.
/// </summary>
public sealed class FormationDetectorScorer
{
    /// <summary>The snapshot extra the 08:00 score is written to, so live rows carry it for the shadow comparison.</summary>
    public const string ScoreExtraKey = "detectorScore";

    /// <summary>How many names the 08:00 alert lists.</summary>
    public const int AlertTop = 10;

    private readonly LogisticModel? model;

    public FormationDetectorScorer(IStaticCatalogSource? catalogs, ILogger<FormationDetectorScorer> log)
    {
        var path = (catalogs ?? StaticCatalogSource.Default).Resolve(FormationDetectorModel.FileName);
        try
        {
            if (!File.Exists(path)) { Error = $"{FormationDetectorModel.FileName} not found at {path}"; return; }
            Meta = FormationDetectorModel.Parse(File.ReadAllText(path));
            model = Meta.ToModel();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Error = ex.Message;
            Meta = null;
            log.LogWarning("Formation detector off: {Error}", ex.Message);
        }
    }

    public FormationDetectorModel? Meta { get; }
    public string? Error { get; }
    public bool Loaded => model is not null;

    /// <summary>The detector's probability for a snapshot; null when the model is off.</summary>
    public double? Score(GapperFormationSnapshot snap) => model?.Predict(FormationDetector.Row(FormationDetector.From(snap)));

    public double? Score(FormationDetectorValues values) => model?.Predict(FormationDetector.Row(values));

    /// <summary>
    /// The detector values of a stored snapshot row (a study panel row or a live recorder row; same table shape), with
    /// TradesRoc from its extras. The study's H11/H11p and the live shadow both read rows through this one mapping.
    /// </summary>
    public static FormationDetectorValues ValuesOf(IdiotProof.Core.Data.GapperFormationSnapshotRow x, double? tradesRoc) => new(
        x.GapPct, x.PmCumVol, x.VolRatioTod, x.OwnProfileRatio, x.Velocity1m, x.Velocity3m, x.Velocity5m, x.Velocity15m, x.Acceleration, x.VolumeRoc,
        x.TradesProxy, tradesRoc, x.SpreadProxy, x.DistToPmHighPct, x.MinsSinceFirstAbnormal,
        x.AbnormalReturnD1, x.AbnormalReturnD2, x.AbnormalReturnD3, x.AbnormalReturnD4, x.AbnormalReturnD5,
        x.VolumeRatioD1, x.VolumeRatioD2, x.VolumeRatioD3, x.VolumeRatioD4, x.VolumeRatioD5,
        x.DaysSinceReverseSplit, x.DaysSince424B, x.DaysSinceS1);

    /// <summary>One line for status output.</summary>
    public string Describe() => Loaded
        ? $"{Meta!.Hypothesis} at {Meta.DecisionTimeEt} ET (validation AUC {Meta.ValidationAuc:0.000} vs benchmark {Meta.ValidationBenchmarkAuc:0.000}, fit on {Meta.TrainRows:N0} train rows); alert lists the top {AlertTop}, watch only"
        : $"off ({Error})";
}
