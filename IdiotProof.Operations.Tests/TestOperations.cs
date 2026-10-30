using IdiotProof.Operations;

namespace IdiotProof.Operations.Tests;

// Fixture operations. They live in the test assembly and are always handed to the catalog by
// explicit type (OperationCatalog.FromTypes), never discovered — the invalid ones would fail it.

public enum TradeSide
{
    Buy,
    ShortSale,
}

/// <summary>Every supported parameter type, as an init-property record.</summary>
public sealed record EverythingArgs
{
    [Argument(0, Description = "Ticker symbol.")]
    public string Ticker { get; init; } = "";

    [Argument(1, Description = "Extra tags.", Required = false)]
    public string[] Tags { get; init; } = [];

    [Option("active", Short = 'a', Description = "Only active ones.")]
    public bool Active { get; init; }

    [Option("limit", Short = 'n', Description = "Max rows.")]
    public int Limit { get; init; } = 50;

    [Option("big", Description = "A long.")]
    public long? Big { get; init; }

    [Option("max-price", Description = "Price cap.")]
    public decimal? MaxPrice { get; init; }

    [Option("ratio", Description = "A double.")]
    public double Ratio { get; init; } = 0.5;

    [Option("id", Description = "Strategy id.")]
    public Guid? Id { get; init; }

    [Option("on", Description = "Trade date.")]
    public DateOnly? On { get; init; }

    [Option("at", Description = "Entry time.")]
    public TimeOnly? At { get; init; }

    [Option("side", Description = "Buy or short.")]
    public TradeSide Side { get; init; } = TradeSide.Buy;

    [Option("maybe-side", Description = "Optional side.")]
    public TradeSide? MaybeSide { get; init; }

    [Option("symbols", Description = "More symbols.")]
    public string[]? Symbols { get; init; }

    [Option("note", Description = "Free text.")]
    public string? Note { get; init; }

    /// <summary>Computed, read-only: ignored by the catalog.</summary>
    public string Display => $"{Ticker}:{Limit}";
}

[Operation("test everything", Summary = "Echo every parameter type.", Risk = OperationRisk.Read)]
[Example("idiotproof test everything AAPL --active --limit 5 --side short-sale")]
[Example("idiotproof test everything MSFT x y --max-price 12.50 --at 09:30 --on 2026-10-30")]
public sealed class EverythingOperation : IOperation<EverythingArgs>
{
    public Task<OperationResult> ExecuteAsync(EverythingArgs args, OperationContext ctx) =>
        Task.FromResult(OperationResult.Ok(args, args.Display));
}

/// <summary>A positional (primary-constructor) record with a required option and defaults.</summary>
public sealed record OrderArgs(
    [property: Argument(0, Description = "Ticker.")] string Ticker,
    [property: Option("qty", Required = true, Description = "Shares.")] int Qty,
    [property: Option("limit-price", Description = "Limit price.")] decimal? LimitPrice = null,
    [property: Option("side", Description = "Side.")] TradeSide Side = TradeSide.ShortSale);

[Operation("test order", Summary = "Pretend to place an order.", Risk = OperationRisk.Money)]
[Example("idiotproof test order AAPL --qty 1")]
public sealed class OrderOperation : IOperation<OrderArgs>
{
    public Task<OperationResult> ExecuteAsync(OrderArgs args, OperationContext ctx)
    {
        if (ctx.ConfirmationCode != "OK123")
            return Task.FromResult(OperationResult.NeedsConfirmation("OK123", $"Buy {args.Qty} {args.Ticker}", ctx.UtcNow.AddMinutes(5)));
        return Task.FromResult(OperationResult.Ok(new { Placed = true, args.Ticker, args.Qty }));
    }
}

/// <summary>Needs an owner; echoes it.</summary>
[Operation("test whoami", Summary = "Return the owner.")]
public sealed class WhoAmIOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) =>
        Task.FromResult(OperationResult.Ok(new { Owner = ctx.RequireOwner() }, ctx.RequireOwner().ToString()));
}

public sealed record ModeArgs
{
    [Option("mode", Required = true, Description = "What to do.")]
    public string Mode { get; init; } = "";
}

/// <summary>Fails in whichever way it is told to, for dispatcher error-mapping tests.</summary>
[Operation("test fail", Summary = "Fail on purpose.", Risk = OperationRisk.Destructive)]
public sealed class FailOperation : IOperation<ModeArgs>
{
    public async Task<OperationResult> ExecuteAsync(ModeArgs args, OperationContext ctx)
    {
        switch (args.Mode)
        {
            case "crash": throw new InvalidOperationException("kaboom\n   at Secret.Stack.Frame()");
            case "usage": throw OperationException.Usage("bad usage");
            case "blocked": throw OperationException.Blocked("market closed");
            case "cancel":
                await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
                return OperationResult.Ok();
            case "null": return null!;
            default: return OperationResult.Fail("failed softly");
        }
    }
}

// ── invalid fixtures (each breaks one catalog rule) ──

[Operation("bad", Summary = "One-word name.")]
public sealed class OneWordOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

[Operation("bad nosummary")]
public sealed class NoSummaryOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

public sealed class NoAttributeOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

public sealed record UndocumentedArgs
{
    [Option("thing")]
    public string? Thing { get; init; }

    [Option("MaxPrice", Description = "Not kebab.")]
    public decimal? MaxPrice { get; init; }

    [Option("json", Description = "Reserved.")]
    public bool Json { get; init; }

    public string? Forgotten { get; init; }

    [Option("weird", Description = "Unsupported type.")]
    public float Weird { get; init; }
}

[Operation("bad args", Summary = "Broken args.")]
public sealed class BadArgsOperation : IOperation<UndocumentedArgs>
{
    public Task<OperationResult> ExecuteAsync(UndocumentedArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

public sealed record GapArgs
{
    [Argument(0, Description = "First.")]
    public string? A { get; init; }

    [Argument(2, Description = "Third.")]
    public string? C { get; init; }
}

[Operation("bad gap", Summary = "Positional gap.")]
public sealed class GapOperation : IOperation<GapArgs>
{
    public Task<OperationResult> ExecuteAsync(GapArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

[Operation("test everything", Summary = "Duplicate name.")]
public sealed class DuplicateOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

[Operation("bad example", Summary = "Example for another op.")]
[Example("idiotproof something else")]
public sealed class BadExampleOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx) => Task.FromResult(OperationResult.Ok());
}

internal static class Fixtures
{
    public static readonly Type[] Valid =
        [typeof(EverythingOperation), typeof(OrderOperation), typeof(WhoAmIOperation), typeof(FailOperation)];

    public static OperationCatalog Catalog() => OperationCatalog.FromTypes(Valid);

    public static OperationDescriptor Everything => Catalog().Find("test everything")!;

    public static OperationDescriptor Order => Catalog().Find("test order")!;

    /// <summary>A clock frozen at 2026-10-30 13:31 UTC = 9:31 AM EDT.</summary>
    public sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public static readonly DateTimeOffset MarketOpen = new(2026, 10, 30, 13, 31, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;
    }
}
