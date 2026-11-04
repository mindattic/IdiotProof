using IdiotProof.Models;

namespace IdiotProof.Core.Evidence;

/// <summary>Which data tiers a set of bars came from, so a study can prove it ran on one consolidated feed.</summary>
public sealed record FeedMix(IReadOnlyDictionary<string, int> Bars)
{
    public int Sip => Count("sip");
    public int Iex => Count("iex");
    public int Unknown => Bars.Where(kv => BarProvenance.TierOf(kv.Key) == "unknown").Sum(kv => kv.Value);

    /// <summary>Both consolidated (SIP) and single-exchange (IEX) bars: their prices and volumes are not comparable.</summary>
    public bool IsMixed => Sip > 0 && Iex > 0;

    public string Describe() => Bars.Count == 0
        ? "no bars"
        : string.Join(", ", Bars.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"));

    private int Count(string tier) => Bars.Where(kv => BarProvenance.TierOf(kv.Key) == tier).Sum(kv => kv.Value);
}

/// <summary>
/// Bars carry their source in <see cref="Candle.Note"/> (<c>Alpaca:sip</c>, <c>Alpaca:iex</c>, <c>Mock</c>…).
/// Rows cached before the tier was recorded read plain <c>Alpaca</c> and count as tier unknown.
/// </summary>
public static class BarProvenance
{
    private const int MaxSourceLength = 16;

    /// <summary>The source to persist for a fetched bar: its own stamp when it has one, else the feed's name.</summary>
    public static string SourceOf(Candle bar, string feedName)
    {
        var source = string.IsNullOrWhiteSpace(bar.Note) ? feedName : bar.Note;
        return source.Length <= MaxSourceLength ? source : source[..MaxSourceLength];
    }

    /// <summary><c>sip</c>, <c>iex</c>, <c>mock</c> or <c>unknown</c>.</summary>
    public static string TierOf(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "unknown";
        var s = source.Trim().ToLowerInvariant();
        if (s.EndsWith(":sip", StringComparison.Ordinal)) return "sip";
        if (s.EndsWith(":iex", StringComparison.Ordinal)) return "iex";
        if (s.StartsWith("mock", StringComparison.Ordinal)) return "mock";
        return "unknown";
    }

    public static FeedMix Mix(IEnumerable<Candle> bars)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in bars)
        {
            var key = string.IsNullOrWhiteSpace(b.Note) ? "(none)" : b.Note;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return new FeedMix(counts);
    }
}
