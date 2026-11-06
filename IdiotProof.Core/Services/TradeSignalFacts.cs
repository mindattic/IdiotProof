namespace IdiotProof.Core.Services;

/// <summary>
/// What the Monitor knows about a symbol beyond the signal itself when it puts the signal to the
/// voter panel: the top catalyst as the catalyst intake scored it, and the formation momentum
/// features as the live recorder computed them. Every field is optional; null lines are not rendered.
/// </summary>
public sealed record TradeSignalFacts
{
    public string? CatalystHeadline { get; init; }
    public double? CatalystScore { get; init; }
    public double? CatalystAgeMinutes { get; init; }
    public int? CatalystDirection { get; init; }
    public string? CatalystPreClass { get; init; }
    public double? Velocity1m { get; init; }
    public double? Velocity3m { get; init; }
    public double? Velocity5m { get; init; }
    public double? Acceleration { get; init; }
    public double? VolRatioTod { get; init; }
    public double? DistFromPmHighPct { get; init; }
    public bool? Halted { get; init; }

    public bool HasCatalyst => CatalystScore is not null || !string.IsNullOrWhiteSpace(CatalystHeadline);

    public bool HasMomentum => Velocity1m is not null || Velocity3m is not null || Velocity5m is not null
        || Acceleration is not null || VolRatioTod is not null || DistFromPmHighPct is not null || Halted is not null;
}
