namespace IdiotProof.Evidence.Gappers;

/// <summary>
/// The time-of-day hazard of the high of day (methods-exit.md 1.3, strategy-refined.md 2.5): for each ET
/// minute m, the share of mornings whose high printed in minute m among those whose high had not printed
/// before m. It is the prior <c>lambda(t)</c> of the drift-change exit, fit on train gapper mornings only
/// (per fold in the exploration, so an eval window never sees its own highs). Counts are pooled over a
/// centred window of <c>smoothMinutes</c> before dividing, so a thin minute does not spike; every minute
/// gets at least <see cref="Floor"/>. Pure and immutable.
/// </summary>
public sealed class HodHazard
{
    public const int MinutesPerDay = 24 * 60;

    private readonly double[] hazard;

    private HodHazard(double[] hazard, int events, double floor)
    {
        this.hazard = hazard;
        Events = events;
        Floor = floor;
    }

    /// <summary>Mornings the hazard was fit on.</summary>
    public int Events { get; }

    /// <summary>The smallest hazard any minute gets (no minute is ever impossible).</summary>
    public double Floor { get; }

    /// <param name="hodMinutesEt">The minute of the ET day (0..1439) in which each fit morning's high printed (bar start).</param>
    /// <param name="smoothMinutes">Width of the centred pooling window (odd; 1 = no smoothing).</param>
    /// <param name="floor">Lower bound on every minute's hazard.</param>
    public static HodHazard Fit(IEnumerable<int> hodMinutesEt, int smoothMinutes = 5, double floor = 1e-4)
    {
        if (smoothMinutes < 1) throw new ArgumentOutOfRangeException(nameof(smoothMinutes), "Smoothing window must be at least one minute.");
        if (floor is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(floor), "Floor must be in (0, 1).");
        var n = new double[MinutesPerDay];
        int events = 0;
        foreach (var m in hodMinutesEt)
        {
            if (m < 0 || m >= MinutesPerDay) continue;
            n[m]++;
            events++;
        }
        var atRisk = new double[MinutesPerDay];
        double tail = 0;
        for (int m = MinutesPerDay - 1; m >= 0; m--)
        {
            tail += n[m];
            atRisk[m] = tail;
        }
        int half = smoothMinutes / 2;
        var h = new double[MinutesPerDay];
        for (int m = 0; m < MinutesPerDay; m++)
        {
            double num = 0, den = 0;
            for (int j = Math.Max(0, m - half); j <= Math.Min(MinutesPerDay - 1, m + half); j++)
            {
                num += n[j];
                den += atRisk[j];
            }
            h[m] = den > 0 ? Math.Clamp(num / den, floor, 1) : floor;
        }
        return new HodHazard(h, events, floor);
    }

    /// <summary>The same hazard every minute (tests, and a clock-free sensitivity).</summary>
    public static HodHazard Constant(double perMinute)
    {
        if (perMinute is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(perMinute), "Hazard must be in (0, 1].");
        var h = new double[MinutesPerDay];
        Array.Fill(h, perMinute);
        return new HodHazard(h, 0, perMinute);
    }

    /// <summary>Hazard of the high printing in the ET minute <paramref name="minuteEt"/> given it has not printed yet.</summary>
    public double At(int minuteEt) => minuteEt < 0 || minuteEt >= MinutesPerDay ? Floor : hazard[minuteEt];

    public double At(TimeSpan timeEt) => At((int)Math.Floor(timeEt.TotalMinutes));

    /// <summary>Hazard of the ET minute that contains <paramref name="utc"/>.</summary>
    public double AtUtc(DateTime utc) => At(MinuteBarMath.ToEastern(utc).TimeOfDay);
}
