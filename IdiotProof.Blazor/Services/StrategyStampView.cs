using IdiotProof.Core.Data;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Services;

/// <summary>
/// What the strategy editor shows in its Stamp card: the same text <c>idiotproof strategy stamp</c>
/// prints, built from the stored canon (<see cref="StrategyLoader"/>, IP-LAW-8), or why there is
/// none (an unsaved strategy, or a quarantined one whose canon can't be read).
/// </summary>
public sealed record StrategyStampView(string? Stamp, string? ShortId, string? Error)
{
    /// <summary>The stamp of a stored strategy's canon (plain ASCII unless <paramref name="unicode"/>).</summary>
    public static StrategyStampView For(Strategy? strategy, bool unicode = false, bool includePayload = true)
    {
        if (strategy is null) return new(null, null, "Save the strategy to see its stamp.");
        return For(strategy.ScriptJson, strategy.ScriptText, unicode, includePayload);
    }

    /// <summary>The stamp of stored canonical JSON / legacy script text.</summary>
    public static StrategyStampView For(string? scriptJson, string? scriptText, bool unicode = false, bool includePayload = true)
    {
        var load = StrategyLoader.Load(scriptJson, scriptText);
        if (load.Definition is not { } def)
            return new(null, null, $"No stamp: the strategy's canon can't be read ({load.CanonicalError ?? "empty"}).");
        try
        {
            return new(def.ToStamp(new StampOptions { Unicode = unicode, IncludePayload = includePayload }), def.ShortId(), null);
        }
        catch (Exception ex)
        {
            return new(null, null, $"No stamp: {ex.Message}");
        }
    }
}
