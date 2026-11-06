using System.Text.Json;
using IdiotProof.Evidence.Gappers;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>
/// One persisted formation snapshot: every core column of <see cref="GapperFormationSnapshot"/>
/// by the same name, plus where it came from, which decision time it answers and what the trigger
/// rule said. The SQL table (another ticket) mirrors these columns one to one.
/// </summary>
public sealed record GapperFormationSnapshotRow
{
    public const string LiveSource = "live";
    public const int CurrentFeatureVersion = 1;

    public const string ScheduleReason = "schedule";
    public const string GatedReason = "gated";
    public const string ControlReason = "control";

    public string Source { get; init; } = LiveSource;
    public required string Symbol { get; init; }
    public required DateOnly DateEt { get; init; }

    /// <summary>The ET clock time the row answers, <c>HH:mm</c>; a schedule time or the minute of an extra row.</summary>
    public required string DecisionTimeEt { get; init; }
    public required DateTime DecisionUtc { get; init; }

    /// <summary>Why the row exists: <see cref="ScheduleReason"/>, <see cref="GatedReason"/> or <see cref="ControlReason"/>.</summary>
    public required string Reason { get; init; }

    public double? GapPct { get; init; }
    public double PmCumVol { get; init; }
    public double? VolRatioTod { get; init; }
    public double? OwnProfileRatio { get; init; }
    public double? Velocity1m { get; init; }
    public double? Velocity3m { get; init; }
    public double? Velocity5m { get; init; }
    public double? Velocity15m { get; init; }
    public double? Acceleration { get; init; }
    public double? VolumeRoc { get; init; }
    public int TradesProxy { get; init; }
    public double? SpreadProxy { get; init; }
    public double? DistToPmHighPct { get; init; }
    public double? MinsSinceFirstAbnormal { get; init; }
    public double? SessionVwap { get; init; }
    public double? AbnormalReturnD1 { get; init; }
    public double? AbnormalReturnD2 { get; init; }
    public double? AbnormalReturnD3 { get; init; }
    public double? AbnormalReturnD4 { get; init; }
    public double? AbnormalReturnD5 { get; init; }
    public double? VolumeRatioD1 { get; init; }
    public double? VolumeRatioD2 { get; init; }
    public double? VolumeRatioD3 { get; init; }
    public double? VolumeRatioD4 { get; init; }
    public double? VolumeRatioD5 { get; init; }
    public int? DaysSinceReverseSplit { get; init; }
    public int? DaysSince424B { get; init; }
    public int? DaysSinceS1 { get; init; }
    public double? CatalystScore { get; init; }
    public double? CatalystAgeMinutes { get; init; }
    public int? CatalystDirection { get; init; }
    public string? CatalystPreClass { get; init; }
    public bool? Halted { get; init; }

    /// <summary>The snapshot's <c>Extra</c> dictionary as a JSON object.</summary>
    public string ExtraJson { get; init; } = "{}";
    public int FeatureVersion { get; init; } = CurrentFeatureVersion;
    public bool TriggerPassed { get; init; }
    public Guid? ArmedStrategyId { get; init; }
    public DateTime WrittenUtc { get; init; }

    public static GapperFormationSnapshotRow FromSnapshot(GapperFormationSnapshot s, string decisionTimeEt, string reason, bool triggerPassed, DateTime writtenUtc, Guid? armedStrategyId = null) => new()
    {
        Symbol = s.Symbol,
        DateEt = s.DateEt,
        DecisionTimeEt = decisionTimeEt,
        DecisionUtc = s.DecisionUtc,
        Reason = reason,
        GapPct = s.GapPct,
        PmCumVol = s.PmCumVol,
        VolRatioTod = s.VolRatioTod,
        OwnProfileRatio = s.OwnProfileRatio,
        Velocity1m = s.Velocity1m,
        Velocity3m = s.Velocity3m,
        Velocity5m = s.Velocity5m,
        Velocity15m = s.Velocity15m,
        Acceleration = s.Acceleration,
        VolumeRoc = s.VolumeRoc,
        TradesProxy = s.TradesProxy,
        SpreadProxy = s.SpreadProxy,
        DistToPmHighPct = s.DistToPmHighPct,
        MinsSinceFirstAbnormal = s.MinsSinceFirstAbnormal,
        SessionVwap = s.SessionVwap,
        AbnormalReturnD1 = s.AbnormalReturnD1,
        AbnormalReturnD2 = s.AbnormalReturnD2,
        AbnormalReturnD3 = s.AbnormalReturnD3,
        AbnormalReturnD4 = s.AbnormalReturnD4,
        AbnormalReturnD5 = s.AbnormalReturnD5,
        VolumeRatioD1 = s.VolumeRatioD1,
        VolumeRatioD2 = s.VolumeRatioD2,
        VolumeRatioD3 = s.VolumeRatioD3,
        VolumeRatioD4 = s.VolumeRatioD4,
        VolumeRatioD5 = s.VolumeRatioD5,
        DaysSinceReverseSplit = s.DaysSinceReverseSplit,
        DaysSince424B = s.DaysSince424B,
        DaysSinceS1 = s.DaysSinceS1,
        CatalystScore = s.CatalystScore,
        CatalystAgeMinutes = s.CatalystAgeMinutes,
        CatalystDirection = s.CatalystDirection,
        CatalystPreClass = s.CatalystPreClass,
        Halted = s.Halted,
        ExtraJson = JsonSerializer.Serialize(s.Extra),
        TriggerPassed = triggerPassed,
        ArmedStrategyId = armedStrategyId,
        WrittenUtc = writtenUtc,
    };
}
