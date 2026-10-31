using IdiotProof.Core.Trading;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyCloneArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol of the strategy to copy.")]
    public string Strategy { get; init; } = "";

    [Option("symbol", Description = "Trade a different symbol with the same rules.")]
    public string? Symbol { get; init; }

    [Option("title", Description = "Title for the copy (default: \"<title> (copy)\").")]
    public string? Title { get; init; }

    [Option("mode", Description = "Broker mode for the copy: sandbox (default) or paper.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;
}

[Operation("strategy clone", Summary = "Copy a strategy (optionally onto another symbol) as a new, inactive Sandbox strategy.", Risk = OperationRisk.Write,
    Description = "The copy gets the same canon (re-validated), never the original's position, history or Live routing. Cloning onto another symbol warns about fixed prices that belonged to the original's chart.")]
[Example("idiotproof strategy clone NVDA")]
[Example("idiotproof strategy clone NVDA --symbol AMD --title \"AMD VWAP reclaim\"")]
public sealed class StrategyCloneOperation : IOperation<StrategyCloneArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyCloneArgs args, OperationContext ctx)
    {
        StrategyDrafts.RequireStartMode(args.Mode);
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var def = StrategyDrafts.Copy(StrategyRefs.RequireDefinition(s));

        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(args.Symbol) && !args.Symbol.Trim().Equals(def.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            var fixedPrices = FixedPrices(def);
            def.Symbol = args.Symbol.Trim().ToUpperInvariant();
            if (fixedPrices.Count > 0)
                notes.Add($"It uses fixed prices from {s.Symbol}'s chart ({string.Join(", ", fixedPrices)}); set ones that fit {def.Symbol} with `strategy set` before activating.");
        }

        var v = StrategyDrafts.Validate(def);
        if (v.HasErrors)
            return OperationResult.Fail("The copy doesn't validate; nothing saved:\n" + v.Render(ctx.Width), new { Saved = false, v.Findings });

        var title = args.Title ?? $"{StrategyArchive.Unmark(s.Title)} (copy)";
        if (!string.Equals(def.Symbol, s.Symbol, StringComparison.OrdinalIgnoreCase) && args.Title is null)
            title = $"{def.Symbol} — {StrategyArchive.Unmark(s.Title)}";
        var created = await StrategyDrafts.SaveAsync(ctx, def, title, args.Mode, "clone", description: s.Description, author: s.Author);
        var shortId = StrategyRefs.ShortGuid(created.Id);
        var human = $"Cloned {s.Symbol} '{s.Title}' ({StrategyRefs.ShortGuid(s.Id)}) → {def.Symbol} '{created.Title}' as {shortId} ({v.StampId}), inactive, {args.Mode}." +
                    string.Concat(notes.Select(n => "\nNote: " + n)) +
                    $"\nActivate it with `idiotproof strategy activate {shortId}`.";
        return OperationResult.Ok(new { created.Id, Short = shortId, From = s.Id, Stamp = v.StampId, def.Symbol, created.Title, Mode = args.Mode.ToString(), Notes = notes }, human);
    }

    /// <summary>Absolute price levels the strategy carries (stop, target, ladder, entry/level conditions).</summary>
    internal static List<string> FixedPrices(StrategyDefinition def)
    {
        var list = new List<string>();
        if (def.StopLossPrice is { } sp) list.Add($"stop {StrategyDrafts.Fmt(sp)}");
        if (def.TakeProfitPrice is { } tp) list.Add($"target {StrategyDrafts.Fmt(tp)}");
        list.AddRange(def.TakeProfitTargets.Select(t => $"{t.Label} {StrategyDrafts.Fmt(t.Price)}"));
        foreach (var c in def.EntryConditions)
        {
            switch (c)
            {
                case PriceCondition p: list.Add($"entry {StrategyDrafts.Fmt(p.Price)}"); break;
                case PriceLevelCondition l: list.Add($"{l.Type} {StrategyDrafts.Fmt(l.Level)}"); break;
                case PatternCondition { Level: { } lvl } pc: list.Add($"{pc.Type} {StrategyDrafts.Fmt(lvl)}"); break;
                case PriceBandCondition b: list.Add($"band {StrategyDrafts.Fmt(b.Min)}–{StrategyDrafts.Fmt(b.Max)}"); break;
            }
        }
        return list;
    }
}
