using IdiotProof.Core.Trading;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyCreateArgs
{
    [Option("script", Description = "The strategy as IdiotScript text (`-` reads stdin).", Short = 's')]
    public string? Script { get; init; }

    [Option("canon", Description = "The strategy as canonical JSON (what `strategy export --format json` prints) (`-` reads stdin).")]
    public string? Canon { get; init; }

    [Option("title", Description = "Title (default: the strategy's own name, else \"SYM strategy\").")]
    public string? Title { get; init; }

    [Option("ticker", Description = "Symbol to use when the script doesn't name one.")]
    public string? Ticker { get; init; }

    [Option("description", Description = "Free-form description stored with the strategy.")]
    public string? Description { get; init; }

    [Option("mode", Description = "Broker mode: sandbox (default) or paper. Live is a separate, confirmed step.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("dry-run", Description = "Validate and show the stamp without saving anything.")]
    public bool DryRun { get; init; }
}

[Operation("strategy create", Summary = "Create a strategy from IdiotScript or canonical JSON after validating it. It starts inactive in Sandbox.", Risk = OperationRisk.Write,
    Description = "Exactly one of --script or --canon. A strategy with no size buys 1 share. Errors (no stop, stop on the wrong side of the target, a broken canon) stop the save; warnings are shown.")]
[Example("idiotproof strategy create --script \"Stock.Ticker(\\\"NVDA\\\").IsAboveVwap().Long().StopLoss(170).TakeProfit(190).Build()\"")]
[Example("idiotproof strategy create --canon - --mode paper", Description = "Reads canonical JSON from stdin.")]
public sealed class StrategyCreateOperation : IOperation<StrategyCreateArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyCreateArgs args, OperationContext ctx)
    {
        ctx.RequireOwner();
        var hasScript = !string.IsNullOrWhiteSpace(args.Script);
        var hasJson = !string.IsNullOrWhiteSpace(args.Canon);
        if (hasScript == hasJson)
            return OperationResult.Usage("Pass exactly one of --script \"<IdiotScript>\" or --canon \"<canonical JSON>\".");
        StrategyDrafts.RequireStartMode(args.Mode);

        var def = hasScript ? StrategyDrafts.FromScript(args.Script!) : StrategyDrafts.FromJson(args.Canon!);
        var applied = StrategyDrafts.ApplyDefaults(def, args.Ticker);
        var validation = StrategyDrafts.Validate(def);
        var stamp = def.ToStamp(new StampOptions { IncludePayload = false });

        var report = validation.Render(ctx.Width);
        var defaults = applied.Count == 0 ? "" : $"Defaults applied: {string.Join(", ", applied)}.\n";
        if (validation.HasErrors)
            return OperationResult.Fail("The strategy has errors and was not saved:\n" + report,
                new { Saved = false, Stamp = validation.StampId, validation.Findings, validation.Script });

        if (args.DryRun)
            return OperationResult.Ok(new { Saved = false, Stamp = validation.StampId, def.Symbol, Applied = applied, validation.Findings, validation.Risk, validation.Script },
                stamp + "\n" + defaults + report + "\n\nDry run: nothing saved.");

        var created = await StrategyDrafts.SaveAsync(ctx, def, args.Title, args.Mode, "create", args.Description);
        var shortId = StrategyRefs.ShortGuid(created.Id);
        var human = stamp + "\n" + defaults + report +
                    $"\n\nCreated {def.Symbol} '{created.Title}' as {shortId} ({validation.StampId}), inactive, {args.Mode}." +
                    $"\nActivate it with `idiotproof strategy activate {shortId}`.";
        return OperationResult.Ok(new
        {
            Saved = true, created.Id, Short = shortId, Stamp = validation.StampId, def.Symbol, created.Title,
            Mode = args.Mode.ToString(), Applied = applied, validation.Findings, validation.Risk,
        }, human);
    }
}
