using System.Text.Json;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Dsl;

/// <summary>The one text argument every <c>dsl</c> converter takes.</summary>
public sealed record DslTextArgs
{
    [Argument(0, Name = "text", Description = "IdiotScript, canonical JSON or a stamp; `-` reads it from stdin.")]
    public string Text { get; init; } = "";
}

/// <summary>The text argument of <c>dsl validate</c> and <c>dsl parse</c>, plus the opt-in to take the corrected script.</summary>
public sealed record DslCheckArgs
{
    [Argument(0, Name = "text", Description = "IdiotScript, canonical JSON or a stamp; `-` reads it from stdin.")]
    public string Text { get; init; } = "";

    [Option("fix", Description = "Apply every determinable fix to the IdiotScript and return the corrected script as the result (nothing is saved). Without it the fixes and the corrected script are only reported.")]
    public bool Fix { get; init; }
}

/// <summary>
/// IdiotScript text to the canonical strategy JSON (IP-LAW-8): the strict, versioned form the
/// Monitor actually evaluates. Also accepts JSON (normalized) or a pasted stamp. Nothing is saved.
/// Each diagnostic carries its fix when one is determinable, and the whole corrected script rides
/// along when the fixes leave a clean script (IP-T523); <c>--fix</c> parses that corrected script
/// instead of the text as written.
/// </summary>
[Operation("dsl parse", Summary = "Parse IdiotScript (or JSON or a stamp) into the canonical strategy JSON the Monitor evaluates; nothing is saved.", Risk = OperationRisk.Read)]
[Example("idiotproof dsl parse \"Ticker(\\\"NVDA\\\").IsAboveVwap().Long().StopLoss(2).TakeProfit(5)\"")]
[Example("idiotproof dsl parse -", Description = "Reads the script from stdin, e.g. `type nvda.idiot | idiotproof dsl parse -`.")]
[Example("idiotproof dsl parse \"Ticker(\\\"NVDA\\\").IsAbveVwap().Long().StopLoss(2)\" --fix", Description = "Parses the corrected script (IsAboveVwap) and returns it with the fixes that were applied.")]
public sealed class DslParseOperation : IOperation<DslCheckArgs>
{
    public Task<OperationResult> ExecuteAsync(DslCheckArgs args, OperationContext ctx)
    {
        var repair = StrategyImportOperation.IsScriptText(args.Text) ? ScriptDiagnostics.Repair(args.Text) : null;
        var isFixed = args.Fix && repair?.CorrectedScript is not null;
        var text = isFixed ? repair!.CorrectedScript! : args.Text;

        var (def, source) = StrategyImportOperation.Parse(text);
        var canon = StrategyJson.Serialize(def);
        using var doc = JsonDocument.Parse(canon);
        var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var data = new
        {
            source, stamp = def.ShortId(), symbol = def.Symbol, canon = doc.RootElement.Clone(),
            diagnostics = repair is null ? [] : DslRepair.Data(repair).Diagnostics,
            fixes = repair?.Fixes ?? [],
            correctedScript = repair?.CorrectedScript,
            @fixed = isFixed,
            script = isFixed ? text : null,
        };

        string trailer;
        if (repair is null || repair.Diagnostics.Count == 0) trailer = "";
        else if (isFixed)
            trailer = $"\n\nFixed: {repair.Fixes.Count} fix(es) applied; the JSON above is the corrected script (nothing was saved):\n{text}\n\nWhat was wrong with the text as written:"
                      + "\n" + string.Join("\n", repair.Diagnostics.Select(d => d.ToString()));
        else
            trailer = "\n\nThe parser skipped or misread part of this script, so the JSON above may not be what you meant:"
                      + DslRepair.Render(repair)
                      + (repair.CorrectedScript is null ? (args.Fix ? "\n\n--fix changed nothing: there is no corrected script to parse." : "")
                          : "\n\nRe-run with --fix to parse the corrected script instead.");
        return Task.FromResult(OperationResult.Ok(data, $"{def.Symbol} {def.ShortId()} (from {source})\n{pretty}{trailer}"));
    }
}
