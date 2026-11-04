using IdiotProof.Models;

namespace IdiotProof.Evidence.Adaptation;

/// <summary>How much of the period must have printed before anything is generated or adapted.</summary>
public sealed record InformationGateOptions
{
    /// <summary>Closed bars required since the period started.</summary>
    public int MinBars { get; init; } = 15;

    /// <summary>Shares traded in those closed bars.</summary>
    public decimal MinVolume { get; init; }

    /// <summary>Wall-clock time that must have passed since the period started.</summary>
    public TimeSpan MinElapsed { get; init; } = TimeSpan.FromMinutes(15);
}

public readonly record struct GateDecision(bool Ready, int ClosedBars, decimal Volume, string Reason);

/// <summary>
/// "Wait for enough information": the first thin minutes of a period act on data that hasn't
/// locked in yet, so nothing adapts until enough closed bars, volume and time have accumulated.
/// Only bars that had fully closed by <c>now</c> count; a still-forming bar is never evidence.
/// </summary>
public static class InformationGate
{
    public static GateDecision Check(IReadOnlyList<Candle> candles, DateTime periodStartUtc, DateTime nowUtc,
        InformationGateOptions? options = null)
    {
        options ??= new InformationGateOptions();
        int bars = 0;
        decimal volume = 0;
        foreach (var c in candles)
        {
            if (c.StartUtc < periodStartUtc || c.EndUtc > nowUtc) continue;
            bars++;
            volume += c.Volume;
        }

        var elapsed = nowUtc - periodStartUtc;
        if (elapsed < options.MinElapsed)
            return new(false, bars, volume, $"only {elapsed.TotalMinutes:0} of {options.MinElapsed.TotalMinutes:0} minutes into the period");
        if (bars < options.MinBars)
            return new(false, bars, volume, $"only {bars} of {options.MinBars} closed bars");
        if (volume < options.MinVolume)
            return new(false, bars, volume, $"only {volume:N0} of {options.MinVolume:N0} shares traded");
        return new(true, bars, volume, "enough information");
    }

    /// <summary>The bars that had fully closed by <paramref name="nowUtc"/>, oldest first: the only bars adaptation may read.</summary>
    public static List<Candle> ClosedBars(IEnumerable<Candle> candles, DateTime nowUtc) =>
        candles.Where(c => c.EndUtc <= nowUtc).OrderBy(c => c.StartUtc).ToList();
}
