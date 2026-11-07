namespace IdiotProof.Evidence.Gappers;

/// <summary>A literature feature computed beside the fixed snapshot columns; null when not computable.</summary>
public interface IGapperFeature
{
    string Name { get; }
    double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core);
}

/// <summary>
/// Ordered list of extra features; <see cref="Compute"/> fills <see cref="GapperFormationSnapshot.Extra"/>.
/// A feature added after the protocol freeze is registered with <c>postRegistration: true</c> so every
/// report marks it as such.
/// </summary>
public sealed class GapperFeatureRegistry
{
    private readonly List<IGapperFeature> features = [];
    private readonly HashSet<string> postRegistration = new(StringComparer.Ordinal);

    public IReadOnlyList<IGapperFeature> Features => features;

    /// <summary>Names of the features declared after the freeze.</summary>
    public IReadOnlyCollection<string> PostRegistrationNames => postRegistration;

    public GapperFeatureRegistry Add(IGapperFeature feature, bool postRegistration = false)
    {
        if (features.Any(f => f.Name == feature.Name))
            throw new ArgumentException($"A feature named '{feature.Name}' is already registered.");
        features.Add(feature);
        if (postRegistration) this.postRegistration.Add(feature.Name);
        return this;
    }

    public bool IsPostRegistration(string name) => postRegistration.Contains(name);

    /// <summary>The two trivial literature features that prove the hook: prior-day overnight return and volume ratio.</summary>
    public static GapperFeatureRegistry Default() => new GapperFeatureRegistry()
        .Add(new OvernightReturnD1Feature())
        .Add(new PriorDayVolumeRatioFeature());

    public IReadOnlyDictionary<string, double> Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var extra = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in features)
            if (f.Compute(inputs, core) is { } v && double.IsFinite(v)) extra[f.Name] = v;
        return extra;
    }
}

/// <summary>Prior day's close-to-open gap in percent: open(D-1) over close(D-2).</summary>
public sealed class OvernightReturnD1Feature : IGapperFeature
{
    public string Name => "overnightReturnD1";

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var daily = inputs.DailyBars;
        if (daily.Count < 2) return null;
        var open = daily[^1].Open;
        var prior = daily[^2].Close;
        return open > 0 && prior > 0 ? (double)(open / prior - 1m) * 100.0 : null;
    }
}

/// <summary>Prior day's volume over the mean of the 20 days before it.</summary>
public sealed class PriorDayVolumeRatioFeature : IGapperFeature
{
    public string Name => "priorDayVolumeRatio";

    public double? Compute(GapperFeatureInputs inputs, GapperFormationSnapshot core)
    {
        var daily = inputs.DailyBars;
        if (daily.Count < 2) return null;
        int from = Math.Max(0, daily.Count - 21);
        decimal sum = 0;
        int n = 0;
        for (int i = from; i < daily.Count - 1; i++) { sum += daily[i].Volume; n++; }
        return n > 0 && sum > 0 ? (double)(daily[^1].Volume / (sum / n)) : null;
    }
}
