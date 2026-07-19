namespace IdiotProof.Scripting;

/// <summary>
/// Outcome of materializing a stored strategy for evaluation/preview.
/// <para><c>FromCanonicalJson</c>: true when the canonical JSON produced the
/// definition.</para>
/// <para><c>CanonicalError</c>: non-null when canonical JSON was PRESENT but
/// rejected — the strategy is quarantined. Callers must not fall back to the
/// tolerant text parse, because "we didn't fully understand it" must never
/// degrade into "evaluate whatever fragments the regex salvaged".</para>
/// </summary>
public sealed record StrategyLoadResult(
    StrategyDefinition? Definition,
    bool FromCanonicalJson,
    string? CanonicalError);

/// <summary>
/// The single materialization path for stored strategies (IP-LAW-8):
/// canonical JSON first, strict; tolerant text parse ONLY for legacy rows
/// that have no canonical JSON at all.
/// </summary>
public static class StrategyLoader
{
    public static StrategyLoadResult Load(string? scriptJson, string? scriptText)
    {
        if (!string.IsNullOrWhiteSpace(scriptJson))
        {
            try
            {
                return new StrategyLoadResult(StrategyJson.Deserialize(scriptJson), true, null);
            }
            catch (StrategyJsonException ex)
            {
                // Fail closed: a present-but-broken canon quarantines the row.
                return new StrategyLoadResult(null, false, ex.Message);
            }
        }

        // Legacy row (pre-canon): the tolerant parser's one remaining job.
        var parsed = string.IsNullOrWhiteSpace(scriptText) ? null : ScriptParser.ParseScript(scriptText);
        return new StrategyLoadResult(parsed, false, null);
    }
}
