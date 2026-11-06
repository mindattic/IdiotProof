namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Whether an item's published timestamp is the moment it went out or a later batch ingestion
/// stamp. Measured live on 2026-11-06: Alpaca news from <c>fiscal.ai</c> (the wire-release copies:
/// GlobeNewswire, PRNewswire, Business Wire, ACCESSWIRE, Newsfile URLs) carries half-hour batch
/// stamps clustered at :02-:03 and :31-:33, so a release flashed by Benzinga Newsdesk at 08:31
/// shows 09:03, and a prior-evening PR shows the next morning. Benzinga-authored items are near
/// real time; EDGAR acceptance stamps are exact. A <c>batch</c> stamp is an upper bound on the
/// publication time and is never treated as exact.
/// </summary>
public static class CatalystStampKind
{
    public const string Realtime = "realtime";
    public const string Batch = "batch";
    public const string Unknown = "unknown";

    public static readonly string[] BatchSources = ["fiscal.ai"];
    public static readonly string[] RealtimeSources = ["benzinga"];
    public static readonly string[] WireDomains = ["globenewswire.com", "prnewswire.com", "businesswire.com", "accesswire.com", "newsfilecorp.com", "newsfile.com"];

    public static string Classify(string source, string? sourceName, string? url)
    {
        if (string.Equals(source, CatalystIntake.Edgar, StringComparison.OrdinalIgnoreCase)) return Realtime;
        var name = (sourceName ?? "").Trim();
        if (BatchSources.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase))) return Batch;
        if (RealtimeSources.Any(r => name.Contains(r, StringComparison.OrdinalIgnoreCase))) return Realtime;
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && WireDomains.Any(d => uri.Host.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
            return Batch;
        return Unknown;
    }

    public static bool IsUpperBound(string? stampKind) => string.Equals(stampKind, Batch, StringComparison.OrdinalIgnoreCase);
}
