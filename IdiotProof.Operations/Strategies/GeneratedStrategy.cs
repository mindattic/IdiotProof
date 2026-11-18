using IdiotProof.Core.Services;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

/// <summary>
/// A model-written strategy that passed every check: the script as accepted (the model's text, or
/// that text with the deterministic fixes applied), the definition it parses to, the save-time
/// verdict (canon, stamp, findings, with no error among them) and the house defaults applied.
/// </summary>
public sealed record GeneratedStrategy(string Script, StrategyDefinition Definition, StrategyValidation Validation, IReadOnlyList<string> Applied);

/// <summary>
/// The validator the generator repair loop runs on every script a model writes (IP-T524). A script
/// is accepted only when all of this holds: <see cref="ScriptDiagnostics"/> reports no error, the
/// parser reads it, and <see cref="StrategyDrafts.Validate"/> finds no error (the canon is written
/// and read back by the strict reader, a stop exists, and the other save-time guards).
/// <para>
/// The line between a fix made here and a model round: the output is corrected without the model
/// only when <see cref="ScriptDiagnostics.Repair"/> offers a whole corrected script (every edit is
/// the single correct one for its diagnostic: a misspelled verb with one closest match, a missing
/// or extra parenthesis, an unterminated string, a quoted number, ignored arguments, a missing
/// EndIf) AND that corrected script passes the same full validation. Those edits change spelling
/// and punctuation only. Everything else is a model round or a failure: an error with no single
/// edit, a script that does not parse, and every guard finding (no stop, a stop on the wrong side,
/// a percent out of range, a bad symbol). Nothing here adds a stop, a size, a direction or a symbol.
/// </para>
/// </summary>
public static class GeneratedStrategyCheck
{
    public static GenerationCheck<GeneratedStrategy> Inspect(string output, string ticker)
    {
        var repair = ScriptDiagnostics.Repair(output);
        var (asWritten, errors) = Judge(output, repair.Diagnostics, ticker);
        if (asWritten is not null) return GenerationCheck<GeneratedStrategy>.Valid(asWritten);

        var fixes = repair.Fixes.Select(Describe).ToList();
        if (repair.CorrectedScript is not { } corrected)
            return GenerationCheck<GeneratedStrategy>.Rejected(errors, fixes);

        var (repaired, remaining) = Judge(corrected, ScriptDiagnostics.Analyze(corrected), ticker);
        if (repaired is not null) return GenerationCheck<GeneratedStrategy>.Corrected(repaired, errors, fixes);

        // The form fixes are right but the corrected script still breaks a guard: the model decides.
        return GenerationCheck<GeneratedStrategy>.Rejected(
            [.. errors, .. remaining.Select(e => "after the proposed fixes: " + e)],
            [.. fixes, "With those fixes the script reads: " + corrected]);
    }

    /// <summary>The accepted strategy, or every reason this text is not one.</summary>
    private static (GeneratedStrategy? Strategy, List<string> Errors) Judge(string script, IReadOnlyList<ScriptDiagnostic> diagnostics, string ticker)
    {
        // A syntax error means the tolerant parser would drop or misread part of the script, so
        // its guard findings would describe a strategy the model did not write. Report syntax first.
        var errors = diagnostics.Where(d => d.IsError)
            .Select(d => $"line {d.Line}, col {d.Column}: {d.Code} {d.Message}" + (d.Suggestion is null ? "" : $" Did you mean '{d.Suggestion}'?"))
            .ToList();
        if (errors.Count > 0) return (null, errors);

        StrategyDefinition def;
        try { def = StrategyDrafts.FromScript(script); }
        catch (OperationException ex) { return (null, [ex.Message]); }

        var applied = StrategyDrafts.ApplyDefaults(def, ticker);
        var validation = StrategyDrafts.Validate(def);
        if (validation.HasErrors)
            return (null, validation.Findings.Where(f => f.Level == FindingLevel.Error).Select(f => $"{f.Rule}: {f.Message}").ToList());
        return (new GeneratedStrategy(script, def, validation, applied), []);
    }

    private static string Describe(ScriptFix fix) => $"line {fix.Line}, col {fix.Column}: {fix.Code} {fix.Description}";
}
