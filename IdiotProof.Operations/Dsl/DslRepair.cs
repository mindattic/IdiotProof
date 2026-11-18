using System.ComponentModel;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Dsl;

/// <summary>One IdiotScript diagnostic as the operations return it: where, what, and the fix when one is determinable.</summary>
[Description("One problem in an IdiotScript.")]
public sealed record DslDiagnostic(
    [property: Description("error or warning. An error means the script is refused.")] string Severity,
    [property: Description("The diagnostic code (IPS001...); dsl.diagnostics in the agent catalog explains each.")] string Code,
    [property: Description("1-based line of the problem.")] int Line,
    [property: Description("1-based column of the problem.")] int Column,
    [property: Description("What is wrong.")] string Message,
    [property: Description("The closest known verb, for an unknown verb; null otherwise.")] string? Suggestion,
    [property: Description("The edit that corrects this problem; null when there is none or more than one.")] ScriptFix? Fix)
{
    public static DslDiagnostic From(ScriptDiagnostic d) =>
        new(d.IsError ? "error" : "warning", d.Code, d.Line, d.Column, d.Message, d.Suggestion, d.Fix);
}

/// <summary>
/// The self-repair payload of a script (IP-T523), the same shape wherever a script error is
/// reported: its diagnostics, the fixes they carry (span plus replacement text) and the whole
/// corrected script when applying them leaves a script that re-analyzes clean. It is data for the
/// caller to resend; no operation that saves or activates a strategy applies it.
/// </summary>
[Description("The self-repair payload of an IdiotScript, the same shape wherever a script error is reported. Nothing in it was applied for the caller.")]
public sealed record DslRepairData(
    [property: Description("Every diagnostic of the text as written.")] IReadOnlyList<DslDiagnostic> Diagnostics,
    [property: Description("The fixes those diagnostics carry.")] IReadOnlyList<ScriptFix> Fixes,
    [property: Description("The whole script with every fix applied, only when it re-analyzes with no error and parses; otherwise null.")] string? CorrectedScript);

/// <summary>Builds and prints the self-repair payload for the <c>dsl</c> and <c>strategy</c> operations.</summary>
public static class DslRepair
{
    public static DslRepairData Data(ScriptRepair repair) =>
        new([.. repair.Diagnostics.Select(DslDiagnostic.From)], repair.Fixes, repair.CorrectedScript);

    /// <summary>Every diagnostic (with its fix line), then the corrected script when there is one; empty when the script is clean.</summary>
    public static string Render(ScriptRepair repair)
    {
        if (repair.Diagnostics.Count == 0) return "";
        return "\n" + string.Join("\n", repair.Diagnostics.Select(d => d.ToString())) + Corrected(repair);
    }

    /// <summary>The "here is the corrected script" block, or why there is none.</summary>
    public static string Corrected(ScriptRepair repair)
    {
        if (repair.CorrectedScript is { } script)
            return "\n\nCorrected script (the fixes above applied and re-checked; nothing was saved or changed):\n" + script;
        if (repair.Fixes.Count > 0 && repair.HasErrors)
            return "\n\nNo corrected script: the fixes above do not clear every error, so the rest needs a decision.";
        return "";
    }
}
