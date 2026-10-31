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

/// <summary>
/// IdiotScript text to the canonical strategy JSON (IP-LAW-8): the strict, versioned form the
/// Monitor actually evaluates. Also accepts JSON (normalized) or a pasted stamp. Nothing is saved.
/// </summary>
[Operation("dsl parse", Summary = "Parse IdiotScript (or JSON or a stamp) into the canonical strategy JSON the Monitor evaluates; nothing is saved.", Risk = OperationRisk.Read)]
[Example("idiotproof dsl parse \"Ticker(\\\"NVDA\\\").IsAboveVwap().Long().StopLoss(2).TakeProfit(5)\"")]
[Example("idiotproof dsl parse -", Description = "Reads the script from stdin, e.g. `type nvda.idiot | idiotproof dsl parse -`.")]
public sealed class DslParseOperation : IOperation<DslTextArgs>
{
    public Task<OperationResult> ExecuteAsync(DslTextArgs args, OperationContext ctx)
    {
        var (def, source) = StrategyImportOperation.Parse(args.Text);
        var canon = StrategyJson.Serialize(def);
        using var doc = JsonDocument.Parse(canon);
        var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var data = new { source, stamp = def.ShortId(), symbol = def.Symbol, canon = doc.RootElement.Clone() };
        return Task.FromResult(OperationResult.Ok(data, $"{def.Symbol} {def.ShortId()} (from {source})\n{pretty}"));
    }
}
