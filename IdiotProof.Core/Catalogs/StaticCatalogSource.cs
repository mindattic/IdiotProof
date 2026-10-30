namespace IdiotProof.Core.Catalogs;

/// <summary>
/// Finds the static JSON catalogs (IP-LAW-7: gapper profiles, S&amp;P index events) for any host.
/// The files live in <c>IdiotProof.Core/Catalogs/</c> and ship to <c>{app}/data/</c> with every
/// host that references IdiotProof.Core, so the web app, the Hub, the Monitor and the scanners all
/// read the same copy without depending on a web root.
/// </summary>
public interface IStaticCatalogSource
{
    /// <summary>
    /// Full path of the catalog file. When no candidate exists the preferred location is returned,
    /// so the caller's missing-file handling (and its log line) names where the file was expected.
    /// </summary>
    string Resolve(string fileName);
}

public sealed class StaticCatalogSource : IStaticCatalogSource
{
    /// <summary>Overrides the catalog directory (e.g. a hand-edited copy outside the install).</summary>
    public const string EnvVar = "IDIOTPROOF_CATALOG_DIR";

    public static StaticCatalogSource Default { get; } = new(AppContext.BaseDirectory);

    private readonly string baseDirectory;

    public StaticCatalogSource(string baseDirectory) => this.baseDirectory = baseDirectory;

    public string Resolve(string fileName)
    {
        var overrideDir = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.Combine(overrideDir, fileName);

        var shipped = Path.Combine(baseDirectory, "data", fileName);
        if (File.Exists(shipped)) return shipped;

        // Hosts published before the catalogs moved into IdiotProof.Core kept them in the web root.
        var legacy = Path.Combine(baseDirectory, "wwwroot", "data", fileName);
        return File.Exists(legacy) ? legacy : shipped;
    }
}
