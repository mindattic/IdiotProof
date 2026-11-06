using IdiotProof.Models;

namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// Knobs of the velocity-exhaustion exit. <see cref="WindowMinutes"/> is 1, 3 or 5; the rule fires
/// once the favorable velocity over that window drops under <see cref="PctPerMin"/>.
/// </summary>
public sealed record VelocityExitParameters(
    double PctPerMin,
    int WindowMinutes = 3,
    TimeSpan? ArmAtEt = null,
    double MinRunPercent = 0,
    bool RequireLowerHigh = false,
    bool RequireVwapLoss = false,
    double? ExtensionFromOpenSwitchPct = null)
{
    public static readonly int[] AllowedWindows = [1, 3, 5];
}

public sealed record VelocityExitVerdict(bool Exit, string Reason, double? Velocity);

/// <summary>
/// "Sell when the run's velocity dies": the exit brain shared by the live Monitor, the day backtester
/// and the study. Fails closed — any missing input means hold. For a short, the favorable direction is
/// down, so returns are negated and the structure checks mirror (a higher low instead of a lower high,
/// a VWAP reclaim instead of a loss).
/// </summary>
public sealed class VelocityExitRule
{
    private readonly VelocityExitParameters p;

    public VelocityExitRule(VelocityExitParameters parameters)
    {
        if (!VelocityExitParameters.AllowedWindows.Contains(parameters.WindowMinutes))
            throw new ArgumentOutOfRangeException(nameof(parameters), $"Velocity window must be one of {string.Join(", ", VelocityExitParameters.AllowedWindows)} minutes.");
        p = parameters;
    }

    public VelocityExitParameters Parameters => p;

    /// <param name="barsSinceEntry">Bars after the entry fill (bars ending after now are ignored).</param>
    /// <param name="entryPrice">The entry fill.</param>
    /// <param name="nowUtc">Evaluation instant.</param>
    /// <param name="sessionVwap">Session VWAP at now; required only when the rule demands a VWAP loss.</param>
    /// <param name="rthOpen">The 09:30 ET open; the extension switch is skipped before the bell.</param>
    /// <param name="shortSide">True for a short: velocity is measured as the favorable (downward) move.</param>
    public VelocityExitVerdict Evaluate(
        IReadOnlyList<Candle> barsSinceEntry,
        decimal entryPrice,
        DateTime nowUtc,
        decimal? sessionVwap = null,
        decimal? rthOpen = null,
        bool shortSide = false)
    {
        if (entryPrice <= 0) return Hold("no entry price");
        var bars = barsSinceEntry.Where(b => b.EndUtc <= nowUtc).ToList();
        if (bars.Count == 0) return Hold("no bars since entry");

        if (p.ArmAtEt is { } arm && MinuteBarMath.ToEastern(nowUtc).TimeOfDay < arm)
            return Hold($"not armed until {arm:hh\\:mm} ET");

        var sign = shortSide ? -1.0 : 1.0;
        var current = bars[^1].Close;
        var extreme = shortSide ? bars.Min(b => b.Low) : bars.Max(b => b.High);
        var runPct = sign * (double)((extreme - entryPrice) / entryPrice) * 100.0;
        if (runPct < p.MinRunPercent)
            return Hold($"run {runPct:F2}% has not reached the {p.MinRunPercent:F2}% minimum");

        var raw = MinuteBarMath.Velocity(bars, nowUtc, p.WindowMinutes);
        if (raw is not { } v) return Hold($"fewer than {p.WindowMinutes} minutes of bars since entry");
        var velocity = sign * v;

        if (p.ExtensionFromOpenSwitchPct is { } switchPct && rthOpen is { } open && open > 0)
        {
            var extension = sign * (double)((current - open) / open) * 100.0;
            if (extension > switchPct)
                return new VelocityExitVerdict(false, $"extended {extension:F1}% from the open (over {switchPct:F1}%): trailing instead", velocity);
        }

        if (p.RequireLowerHigh && !HasLowerExtreme(bars, shortSide))
            return new VelocityExitVerdict(false, shortSide ? "no higher low yet" : "no lower high yet", velocity);

        if (p.RequireVwapLoss)
        {
            if (sessionVwap is not { } vwap) return new VelocityExitVerdict(false, "no session VWAP", velocity);
            var lost = shortSide ? current > vwap : current < vwap;
            if (!lost) return new VelocityExitVerdict(false, shortSide ? "still below VWAP" : "still above VWAP", velocity);
        }

        if (velocity < p.PctPerMin)
            return new VelocityExitVerdict(true,
                $"{p.WindowMinutes}-minute velocity {velocity:F2}%/min fell under {p.PctPerMin:F2}%/min after a {runPct:F1}% run", velocity);

        return new VelocityExitVerdict(false, $"velocity {velocity:F2}%/min still at or above {p.PctPerMin:F2}%/min", velocity);
    }

    private static VelocityExitVerdict Hold(string reason) => new(false, reason, null);

    /// <summary>The newest confirmed swing (two bars each side) sits inside the one before it: a lower high, or for a short a higher low.</summary>
    private static bool HasLowerExtreme(List<Candle> bars, bool shortSide)
    {
        const int piv = 2;
        decimal? older = null, newer = null;
        for (int i = piv; i < bars.Count - piv; i++)
        {
            bool isPivot = true;
            for (int j = i - piv; j <= i + piv && isPivot; j++)
            {
                if (j == i) continue;
                isPivot = shortSide ? bars[j].Low >= bars[i].Low : bars[j].High <= bars[i].High;
            }
            if (!isPivot) continue;
            older = newer;
            newer = shortSide ? bars[i].Low : bars[i].High;
        }
        return older is { } a && newer is { } b && (shortSide ? b > a : b < a);
    }
}

/// <summary>The gapper screen's volume ratio, one formula for the live scanner and the study.</summary>
public static class GapperVolumeRatio
{
    /// <summary>
    /// Cumulative session volume against ADV: time-of-day aware when a profile, decile and clock time
    /// are given, otherwise the plain cumVol / ADV the scanner has always used.
    /// </summary>
    public static double? Compute(double? cumVol, double? avgDailyVolume, TimeOfDayProfile? profile = null, int? advDecile = null, TimeSpan? timeEt = null)
    {
        if (cumVol is not { } vol || avgDailyVolume is not > 0) return null;
        if (profile is { } prof && advDecile is { } decile && timeEt is { } time)
            return prof.VolRatioTod(vol, avgDailyVolume.Value, decile, time);
        return vol / avgDailyVolume.Value;
    }
}
