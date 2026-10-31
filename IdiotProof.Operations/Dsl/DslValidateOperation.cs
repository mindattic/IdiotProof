using IdiotProof.Blazor.Services;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Dsl;

/// <summary>One finding from <c>dsl validate</c>: an error blocks (fail closed), a warning informs.</summary>
public sealed record DslFinding(string Severity, string Phase, string Rule, string Message);

/// <summary>
/// Checks a strategy the way the Monitor and the gates will treat it, without saving or running
/// it. Fail closed: anything the evaluator would refuse or quarantine is an error. The rules:
/// it parses; its canonical JSON round-trips to the same strategy (IP-LAW-8); it names a symbol;
/// it has at least one entry trigger; every condition sits in a phase that may hold it (only
/// Filters and Entry conditions are evaluated before a fire); it carries a protective stop on the
/// right side of its target (the Risk Guardian refuses entries without one, IP-LAW-2); sizes,
/// percents and scale-out shares are in range. Warnings: a verb with no glossary entry, a script
/// view that doesn't parse back exactly, a share count of 0 (sized by the workspace default).
/// </summary>
[Operation("dsl validate", Summary = "Validate IdiotScript/JSON against the phase rules and the gates, failing closed with every reason (nothing is saved).", Risk = OperationRisk.Read)]
[Example("idiotproof dsl validate \"Ticker(\\\"NVDA\\\").IsAboveVwap().Long().StopLoss(2).TakeProfit(5)\"")]
[Example("idiotproof dsl validate -", Description = "Reads the script or JSON from stdin.")]
public sealed class DslValidateOperation : IOperation<DslTextArgs>
{
    private static readonly HashSet<StrategyPhase> PreFirePhases = [StrategyPhase.Filters, StrategyPhase.Entry];

    public Task<OperationResult> ExecuteAsync(DslTextArgs args, OperationContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Text))
            return Task.FromResult(OperationResult.Usage("Pass the IdiotScript or JSON to validate (or `-` to pipe it in)."));

        StrategyDefinition? def = null;
        string source = "?";
        var findings = new List<DslFinding>();
        try
        {
            (def, source) = StrategyImportOperation.Parse(args.Text);
        }
        catch (OperationException ex)
        {
            findings.Add(new("error", "Setup", "parses", ex.Message));
        }

        if (def is not null) findings.AddRange(Check(def));
        var errors = findings.Count(f => f.Severity == "error");
        var valid = errors == 0;

        var table = new TextTable { MaxWidth = ctx.Width }.Column("").Column("Phase").Column("Rule").Column("Finding");
        foreach (var f in findings.OrderBy(f => f.Severity == "error" ? 0 : 1))
            table.Row(f.Severity == "error" ? "ERROR" : "warn", f.Phase, f.Rule, f.Message);
        var head = def is null
            ? "INVALID: the text does not parse."
            : valid
                ? $"VALID: {def.Symbol} {def.Direction} {def.ShortId()} (from {source}), {def.EntryConditions.Count} condition(s)."
                : $"INVALID: {def.Symbol} {def.ShortId()} has {errors} error(s); the Monitor would refuse or quarantine it.";
        var human = head + (findings.Count == 0 ? "" : "\n\n" + table.Render());
        var data = new { valid, source, symbol = def?.Symbol, stamp = def?.ShortId(), errors, warnings = findings.Count - errors, findings };

        return Task.FromResult(valid
            ? OperationResult.Ok(data, human)
            : new OperationResult { Status = OperationStatus.Failed, Data = data, Human = human, Message = $"{errors} validation error(s)." });
    }

    /// <summary>Every rule against a parsed definition.</summary>
    internal static IEnumerable<DslFinding> Check(StrategyDefinition def)
    {
        // IP-LAW-8: the canon must serialize and read back to the very same strategy.
        string? canonError = null;
        try
        {
            var back = StrategyJson.Deserialize(StrategyJson.Serialize(def));
            if (back.ShortId() != def.ShortId()) canonError = "canonical JSON reads back as a different strategy";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            canonError = ex.Message;
        }
        if (canonError is not null) yield return new("error", "Setup", "canon round-trip", canonError + " (it would be quarantined, IP-LAW-8)");

        string? scriptIssue = null;
        try
        {
            if (ScriptParser.ParseScript(def.ToScript()) is not { } reparsed || reparsed.ShortId() != def.ShortId())
                scriptIssue = "the IdiotScript view doesn't parse back exactly; edit it as JSON";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            scriptIssue = "the IdiotScript view doesn't parse back: " + ex.Message;
        }
        if (scriptIssue is not null) yield return new("warn", "Setup", "script round-trip", scriptIssue);

        // Setup
        if (string.IsNullOrWhiteSpace(def.Symbol))
            yield return new("error", "Setup", "symbol", "no Ticker(...): there is nothing to trade");

        // Filters + Entry: what the Monitor evaluates before a fire
        var triggers = def.EntryConditions.Count(c => c.Phase == StrategyPhase.Entry);
        if (def.EntryConditions.Count == 0 && !def.HasBranching)
            yield return new("error", "Entry", "trigger", "no entry conditions: it would never fire");
        else if (triggers == 0 && !def.HasBranching)
            yield return new("warn", "Entry", "trigger", "only Filters (regime gates), no Entry trigger: it fires on the first bar the filters pass");
        foreach (var c in def.EntryConditions)
        {
            string script;
            try { script = c.ToScript(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { script = c.GetType().Name + " (" + ex.Message + ")"; }
            if (!PreFirePhases.Contains(c.Phase))
                yield return new("error", c.Phase.ToString(), "phase", $"{script} is a {c.Phase} verb but sits with the entry conditions; only Filters and Entry are checked before a fire");
            if (DslGlossary.Find(DslVerbsOperation.NameOf(script)) is null)
                yield return new("warn", c.Phase.ToString(), "glossary", $"{DslVerbsOperation.NameOf(script)} has no glossary entry (it won't be explained on the strategy card)");
        }

        // Order
        if (def.NotionalAmount is { } notional && notional <= 0)
            yield return new("error", "Order", "size", "a dollar (notional) size must be positive");
        if (def.Quantity < 0)
            yield return new("error", "Order", "size", "the share count can't be negative");
        if (def.Quantity == 0 && def.NotionalAmount is null)
            yield return new("warn", "Order", "size", "no Quantity(...): the workspace default size is used at fire time");

        // Risk: IP-LAW-2, the Risk Guardian refuses an entry without a protective stop
        var hasStop = def.StopLossPrice is > 0 || def.StopLossPercent is > 0 || def.TrailingStopPercent is > 0;
        if (!hasStop)
            yield return new("error", "Risk", "stop", "no StopLoss/TrailingStop: the Risk Guardian refuses every entry without a stop (IP-LAW-2)");
        if (def.StopLossPercent is { } sp && (sp <= 0 || sp >= 100))
            yield return new("error", "Risk", "stop", $"stop percent {sp} must be between 0 and 100");
        if (def.TrailingStopPercent is { } tp && (tp <= 0 || tp >= 100))
            yield return new("error", "Risk", "stop", $"trailing stop percent {tp} must be between 0 and 100");
        if (def.StopLossPercent is > 10)
            yield return new("warn", "Risk", "stop", $"a {def.StopLossPercent}% stop is wider than the Risk Guardian's default 10% maximum");

        // Exit
        var isLong = def.Direction == TradeDirection.Long;
        if (def.StopLossPrice is { } stop && def.TakeProfitPrice is { } target && stop > 0 && target > 0
            && (isLong ? target <= stop : target >= stop))
            yield return new("error", "Risk", "stop side", $"{def.Direction} with stop {stop} and target {target}: the stop must be {(isLong ? "below" : "above")} the target");
        foreach (var t in def.TakeProfitTargets.Where(t => t.Price <= 0 || t.PercentToSell is <= 0 or > 100))
            yield return new("error", "Exit", "targets", $"{t.Label}: price must be positive and percent to sell 1-100");
        var scaled = def.TakeProfitTargets.Sum(t => t.PercentToSell);
        if (def.TakeProfitTargets.Count > 0 && scaled > 100)
            yield return new("error", "Exit", "targets", $"scale-out targets sell {scaled}% of the position");
        if (def.TakeProfitPercent is { } tpp && tpp <= 0)
            yield return new("error", "Exit", "targets", "take-profit percent must be positive");
        if (def.PeakGivebackPercent is { } g && (g <= 0 || g >= 100))
            yield return new("error", "Exit", "giveback", $"peak giveback {g}% must be between 0 and 100");
    }
}
