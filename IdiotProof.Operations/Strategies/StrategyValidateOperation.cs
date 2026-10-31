using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyValidateArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol of a saved strategy (omit with --script/--canon).", Required = false)]
    public string? Strategy { get; init; }

    [Option("script", Description = "Validate this IdiotScript text instead of a saved strategy (`-` reads stdin).", Short = 's')]
    public string? Script { get; init; }

    [Option("canon", Description = "Validate this canonical JSON instead of a saved strategy (`-` reads stdin).")]
    public string? Canon { get; init; }
}

[Operation("strategy validate", Summary = "Check a saved strategy or a script: parse, canon round-trip, phase rules and a risk preview. Changes nothing.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy validate NVDA")]
[Example("idiotproof strategy validate --script \"Stock.Ticker(\\\"AMD\\\").IsAboveVwap().Long().StopLoss(150).Build()\"")]
public sealed class StrategyValidateOperation : IOperation<StrategyValidateArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyValidateArgs args, OperationContext ctx)
    {
        var sources = new[] { args.Strategy, args.Script, args.Canon }.Count(s => !string.IsNullOrWhiteSpace(s));
        if (sources != 1)
            return OperationResult.Usage("Name one thing to validate: a saved strategy, --script \"<IdiotScript>\" or --canon \"<canonical JSON>\".");

        StrategyDefinition def;
        string subject;
        string? quarantined = null;
        if (!string.IsNullOrWhiteSpace(args.Strategy))
        {
            var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
            var load = StrategyRefs.Load(s);
            if (load.Definition is null)
            {
                quarantined = load.CanonicalError ?? "the script doesn't parse";
                return OperationResult.Ok(new { s.Id, Valid = false, Quarantined = quarantined },
                    $"{s.Symbol} '{s.Title}' is QUARANTINED — the Monitor won't evaluate it: {quarantined}\n" +
                    "Repair the canon with `idiotproof strategy resync-canon --apply`, or recreate it.");
            }
            def = load.Definition;
            subject = $"{s.Symbol} '{s.Title}' ({StrategyRefs.ShortGuid(s.Id)})" + (load.FromCanonicalJson ? "" : " — legacy row with no canon; validated from its script");
        }
        else if (!string.IsNullOrWhiteSpace(args.Script)) { def = StrategyDrafts.FromScript(args.Script!); subject = "the script"; }
        else { def = StrategyDrafts.FromJson(args.Canon!); subject = "the canonical JSON"; }

        var v = StrategyDrafts.Validate(def);
        var errors = v.Findings.Count(f => f.Level == FindingLevel.Error);
        var warnings = v.Findings.Count(f => f.Level == FindingLevel.Warning);
        var verdict = errors > 0 ? $"INVALID — {errors} error(s), {warnings} warning(s)." : warnings > 0 ? $"Valid with {warnings} warning(s)." : "Valid.";
        var human = $"Validating {subject}: {def.Symbol} {v.StampId}\n" +
                    $"canon round-trip: {(v.CanonRoundTrips ? "yes" : "NO")} · script round-trip: {(v.ScriptRoundTrips ? "yes" : "NO")}\n\n" +
                    v.Render(ctx.Width) + "\n\n" + verdict;
        return OperationResult.Ok(new
        {
            Valid = errors == 0, Stamp = v.StampId, def.Symbol, v.CanonRoundTrips, v.ScriptRoundTrips,
            Errors = errors, Warnings = warnings, v.Findings, v.Risk,
        }, human);
    }
}
