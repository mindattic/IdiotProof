namespace IdiotProof.Evidence.Survival;

/// <summary>One observed hold: how long it lasted and what ended it (0 = censored, 1..k = competing cause).</summary>
public readonly record struct SurvivalObservation(double Time, int Cause);

/// <summary>One step of a Kaplan-Meier curve: at <see cref="Time"/>, <see cref="AtRisk"/> holds were open and <see cref="Events"/> ended.</summary>
public readonly record struct KaplanMeierStep(double Time, int AtRisk, int Events, double Survival, double StandardError);

/// <summary>One step of the competing-risk curves: overall survival plus each cause's cumulative incidence.</summary>
public readonly record struct IncidenceStep(double Time, int AtRisk, double Survival, IReadOnlyList<double> Incidence);

/// <summary>
/// Survival analysis of holds (biostatistics applied to exits): how long a position stays open
/// before it hits its target, its stop, or gets cut off (sell-by, end of data) — which censors
/// the observation rather than counting as a win or loss.
/// </summary>
public static class SurvivalAnalysis
{
    /// <summary>
    /// Kaplan-Meier estimate of P(still open after t), any cause counting as the event, with
    /// Greenwood's variance. Censored holds leave the risk set without an event.
    /// </summary>
    public static IReadOnlyList<KaplanMeierStep> KaplanMeier(IEnumerable<SurvivalObservation> observations)
    {
        var obs = observations.Where(o => !double.IsNaN(o.Time)).OrderBy(o => o.Time).ToList();
        var steps = new List<KaplanMeierStep>();
        double s = 1, greenwood = 0;
        int i = 0, atRisk = obs.Count;
        while (i < obs.Count)
        {
            double t = obs[i].Time;
            int events = 0, leaving = 0;
            while (i < obs.Count && obs[i].Time == t)
            {
                if (obs[i].Cause != 0) events++;
                leaving++;
                i++;
            }
            if (events > 0)
            {
                s *= 1.0 - (double)events / atRisk;
                if (atRisk > events) greenwood += (double)events / ((double)atRisk * (atRisk - events));
                steps.Add(new KaplanMeierStep(t, atRisk, events, s, s * Math.Sqrt(greenwood)));
            }
            atRisk -= leaving;
        }
        return steps;
    }

    /// <summary>The first time the survival curve reaches 0.5 or below (NaN if it never does).</summary>
    public static double MedianTime(IReadOnlyList<KaplanMeierStep> curve)
    {
        foreach (var step in curve) if (step.Survival <= 0.5) return step.Time;
        return double.NaN;
    }

    /// <summary>
    /// Aalen-Johansen cumulative incidence per cause: P(the hold ended by cause k by time t) in the
    /// presence of the other causes. At every time the overall survival plus all incidences equals 1.
    /// </summary>
    public static IReadOnlyList<IncidenceStep> CumulativeIncidence(IEnumerable<SurvivalObservation> observations, int causes)
    {
        if (causes < 1) throw new ArgumentOutOfRangeException(nameof(causes));
        var obs = observations.Where(o => !double.IsNaN(o.Time)).OrderBy(o => o.Time).ToList();
        var steps = new List<IncidenceStep>();
        var cif = new double[causes];
        double s = 1;
        int i = 0, atRisk = obs.Count;
        while (i < obs.Count)
        {
            double t = obs[i].Time;
            var byCause = new int[causes];
            int events = 0, leaving = 0;
            while (i < obs.Count && obs[i].Time == t)
            {
                int c = obs[i].Cause;
                if (c > causes) throw new ArgumentException($"Cause {c} exceeds the declared {causes} causes.");
                if (c > 0) { byCause[c - 1]++; events++; }
                leaving++;
                i++;
            }
            if (events > 0)
            {
                for (int k = 0; k < causes; k++) cif[k] += s * byCause[k] / atRisk;
                s *= 1.0 - (double)events / atRisk;
                steps.Add(new IncidenceStep(t, atRisk, s, (double[])cif.Clone()));
            }
            atRisk -= leaving;
        }
        return steps;
    }

    /// <summary>Value of a step curve at time t (the last step at or before t; <paramref name="before"/> before the first step).</summary>
    public static double At<T>(IReadOnlyList<T> curve, double t, Func<T, double> time, Func<T, double> value, double before)
    {
        double v = before;
        foreach (var step in curve)
        {
            if (time(step) > t) break;
            v = value(step);
        }
        return v;
    }

    /// <summary>
    /// The earliest time a cause's incidence reaches <paramref name="share"/> of its final level: past
    /// it, waiting longer adds little chance of that exit. NaN when the cause never occurs.
    /// </summary>
    public static double FlatteningTime(IReadOnlyList<IncidenceStep> curve, int cause, double share = 0.95)
    {
        if (curve.Count == 0) return double.NaN;
        double final = curve[^1].Incidence[cause - 1];
        if (final <= 0) return double.NaN;
        foreach (var step in curve)
            if (step.Incidence[cause - 1] >= share * final - 1e-12) return step.Time;
        return curve[^1].Time;
    }
}
