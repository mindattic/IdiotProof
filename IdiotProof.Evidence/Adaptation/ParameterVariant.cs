namespace IdiotProof.Evidence.Adaptation;

/// <summary>One candidate set of exit parameters for an armed strategy (percents, e.g. 2.5 = 2.5%).</summary>
public sealed record ParameterVariant(string Name, double StopLossPercent, double? TrailingStopPercent, double? TakeProfitPercent);

/// <summary>One simulated round trip a variant would have made on bars that had already closed.</summary>
public readonly record struct ShadowTrade(DateTime EntryUtc, DateTime ExitUtc, double ReturnPercent)
{
    public bool Won => ReturnPercent > 0;
}
