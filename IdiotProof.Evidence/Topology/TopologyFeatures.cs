namespace IdiotProof.Evidence.Topology;

/// <summary>Shape of the recent return dynamics at one bar, read from closed bars only.</summary>
public sealed record TopologyReading(int Index, double H0Total, double H1Total, double H1Max, double H1LandscapeL1, int Points);

/// <summary>Settings for the sliding-window topology read.</summary>
public sealed record TopologySettings(int Window = 40, int Dimension = 3, int Delay = 1, int LandscapeLevels = 3)
{
    public int PointsPerWindow => Window - 1 - (Dimension - 1) * Delay;
}

/// <summary>
/// Per-bar topological summary of the last <c>Window</c> closes (bars i−Window+1..i): their
/// z-normalized log returns are delay-embedded and the Rips persistence of the cloud is summarized.
/// Bar i's reading never looks at bar i+1.
/// </summary>
public static class TopologyFeatures
{
    public static readonly string[] Names = ["h0-total", "h1-total", "h1-max", "h1-landscape-l1"];

    public static TopologyReading? At(IReadOnlyList<double> closes, int index, TopologySettings settings)
    {
        int start = index - settings.Window + 1;
        if (start < 0 || index >= closes.Count) return null;
        var window = new double[settings.Window];
        for (int i = 0; i < settings.Window; i++) window[i] = closes[start + i];
        var cloud = DelayEmbedding.Embed(DelayEmbedding.NormalizedLogReturns(window), settings.Dimension, settings.Delay);
        if (cloud.Length < 3) return null;
        var bars = PersistentHomology.Compute(cloud);
        return new TopologyReading(index,
            PersistenceLandscape.TotalPersistence(bars, 0),
            PersistenceLandscape.TotalPersistence(bars, 1),
            PersistenceLandscape.MaxPersistence(bars, 1),
            PersistenceLandscape.Norms(bars, 1, settings.LandscapeLevels).L1,
            cloud.Length);
    }

    public static double Value(TopologyReading r, string name) => name switch
    {
        "h0-total" => r.H0Total,
        "h1-total" => r.H1Total,
        "h1-max" => r.H1Max,
        "h1-landscape-l1" => r.H1LandscapeL1,
        _ => throw new ArgumentException($"Unknown topology feature '{name}'.", nameof(name)),
    };
}
