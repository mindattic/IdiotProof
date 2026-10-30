using IdiotProof.Operations;

namespace IdiotProof.Cli.Tests;

// Fixture operations standing in for "Hub-only" operations (ones the CLI's own assembly does not
// contain). Handed to the catalog by explicit type, never discovered.

public enum SampleSide
{
    Long,
    ShortSale,
}

public sealed record SampleArgs
{
    [Argument(0, Description = "Ticker symbol.")]
    public string Ticker { get; init; } = "";

    [Argument(1, Description = "Free-form note.", Required = false)]
    public string? Note { get; init; }

    [Option("max-price", Short = 'm', Description = "Highest price to pay.")]
    public decimal MaxPrice { get; init; } = 1.5m;

    [Option("side", Description = "Trade direction.")]
    public SampleSide Side { get; init; } = SampleSide.Long;

    [Option("active", Short = 'a', Description = "Only active ones.")]
    public bool Active { get; init; }

    [Option("starts", Description = "Start time.")]
    public TimeOnly Starts { get; init; } = new(9, 30);

    [Option("owner-name", Description = "Who it is for.", Required = true)]
    public string OwnerName { get; init; } = "";
}

[Operation("strategy sample", Summary = "A sample strategy command.", Description = "Longer description of the sample.", Risk = OperationRisk.Write)]
[Example("idiotproof strategy sample AAPL --owner-name ryan", Description = "The usual case.")]
[Example("idiotproof strategy sample MSFT \"a note\" --owner-name ryan --side short-sale")]
public sealed class SampleStrategyOperation : IOperation<SampleArgs>
{
    public Task<OperationResult> ExecuteAsync(SampleArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

[Operation("strategy list", Summary = "List strategies.")]
public sealed class SampleListOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

internal static class SampleCatalog
{
    /// <summary>The built-in operations plus the samples — what a Hub with more operations serves.</summary>
    public static OperationCatalog Hub() => OperationCatalog.FromTypes(
        OperationCatalog.DiscoverTypes(typeof(OperationCatalog).Assembly)
            .Concat([typeof(SampleStrategyOperation), typeof(SampleListOperation)]));
}
