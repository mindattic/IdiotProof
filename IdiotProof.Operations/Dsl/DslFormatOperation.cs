using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Dsl;

/// <summary>
/// Canonical strategy JSON (or a stamp) to IdiotScript text: the human view generated from the
/// model (IP-LAW-8), InvariantCulture, the same text the web editor and <c>strategy show</c> print.
/// Says so when the text would not parse back to the same strategy.
/// </summary>
[Operation("dsl format", Summary = "Render canonical strategy JSON (or a stamp) as IdiotScript text, checking it parses back to the same strategy.", Risk = OperationRisk.Read)]
[Example("idiotproof dsl format -", Description = "Reads the JSON from stdin, e.g. `idiotproof strategy export INTC --format json | idiotproof dsl format -`.")]
public sealed class DslFormatOperation : IOperation<DslTextArgs>
{
    public Task<OperationResult> ExecuteAsync(DslTextArgs args, OperationContext ctx)
    {
        var (def, source) = StrategyImportOperation.Parse(args.Text);
        var script = def.ToScript();
        bool roundTrips;
        try
        {
            roundTrips = ScriptParser.ParseScript(script) is { } back && back.ShortId() == def.ShortId();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            roundTrips = false;
        }
        var human = script + (roundTrips ? "" : "\n\nNote: this text does not parse back to exactly the same strategy; keep the JSON as the source of truth (IP-LAW-8).");
        return Task.FromResult(OperationResult.Ok(new { source, stamp = def.ShortId(), symbol = def.Symbol, script, roundTrips }, human));
    }
}
