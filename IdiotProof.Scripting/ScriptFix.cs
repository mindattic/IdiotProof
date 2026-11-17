namespace IdiotProof.Scripting;

/// <summary>
/// One machine-applicable correction to an IdiotScript (IP-T523): replace <see cref="Length"/>
/// characters at <see cref="Offset"/> (0-based, in the text that was analyzed) with
/// <see cref="Replacement"/>. A length of 0 is an insertion, an empty replacement a deletion.
/// <see cref="Line"/> and <see cref="Column"/> are the same position 1-based, for people.
/// A diagnostic carries one only when a single correct edit exists; where the right edit is a
/// judgement call (which number, which symbol, where a branch ends) there is none.
/// </summary>
public sealed record ScriptFix(string Code, int Offset, int Length, int Line, int Column, string Replacement, string Description)
{
    /// <summary>The text with just this edit made.</summary>
    public string Apply(string text) => text[..Offset] + Replacement + text[(Offset + Length)..];
}

/// <summary>
/// What <see cref="ScriptDiagnostics.Repair"/> found: every diagnostic of the text as written, the
/// fixes those diagnostics carry, and <see cref="CorrectedScript"/>, the whole script with the
/// fixes applied. The corrected script is offered only when it was re-analyzed and has no errors
/// left and still parses; otherwise it is null and the caller has the per-diagnostic fixes only.
/// Nothing here is ever applied for the caller: saving or activating a strategy never takes it.
/// </summary>
public sealed record ScriptRepair(IReadOnlyList<ScriptDiagnostic> Diagnostics, IReadOnlyList<ScriptFix> Fixes, string? CorrectedScript)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    /// <summary>Errors that carry no fix of their own (a corrected script may still exist when another fix removes their cause).</summary>
    public int UnfixedErrors => Diagnostics.Count(d => d.IsError && d.Fix is null);
}
