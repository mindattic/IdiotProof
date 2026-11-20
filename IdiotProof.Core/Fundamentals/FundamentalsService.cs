using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdiotProof.Core.Fundamentals;

/// <summary>One lookup's outcome: the snapshot (null when the vendor does not know the symbol), where it came from, or the error.</summary>
public sealed record FundamentalsLookup(string Symbol, string? Source, FundamentalsSnapshot? Snapshot, bool FromCache, string? Error);

/// <summary>
/// The float / short-interest front door (IP-T603): the three vendor sources in preference order,
/// a once-per-ET-day file cache in front of them, and "which source answers" (the first configured
/// one, unless the caller names one). Everything here is read-only against the vendors.
/// <para>Preference order: config <c>IdiotProof:Fundamentals:Order</c> (comma-separated source
/// names), else <c>polygon, fmp, finviz-elite</c>.</para>
/// </summary>
public sealed partial class FundamentalsService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly FundamentalsCache cache;
    private readonly TimeProvider clock;

    public FundamentalsService(IReadOnlyList<IFundamentalsSource> sources, FundamentalsCache cache, TimeProvider clock, FundamentalsKeys? keys = null)
    {
        Sources = sources;
        this.cache = cache;
        this.clock = clock;
        Keys = keys;
    }

    /// <summary>The production service: Vault keys, the real cache directory, the configured order.</summary>
    public static FundamentalsService Create(IConfiguration? configuration, IHttpClientFactory? httpFactory, TimeProvider? clock)
    {
        clock ??= TimeProvider.System;
        var keys = FundamentalsKeys.Default(configuration);
        Func<HttpClient> http = httpFactory is null ? () => SharedClient : () => httpFactory.CreateClient("fundamentals");
        var all = new IFundamentalsSource[]
        {
            new PolygonFundamentalsSource(keys, http, clock),
            new FmpFundamentalsSource(keys, http, clock),
            new FinvizEliteFundamentalsSource(keys, http, clock),
        };
        var order = (configuration?["IdiotProof:Fundamentals:Order"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant()).ToList();
        var ordered = all.OrderBy(s => order.IndexOf(s.Name) is var i && i >= 0 ? i : order.Count + Array.IndexOf(all, s)).ToList();
        return new FundamentalsService(ordered, new FundamentalsCache(FundamentalsCache.DefaultDirectory), clock, keys);
    }

    /// <summary>Every source, in preference order, configured or not.</summary>
    public IReadOnlyList<IFundamentalsSource> Sources { get; }

    /// <summary>The key resolver (status output names its Tokens file); null in tests.</summary>
    public FundamentalsKeys? Keys { get; }

    public string CacheDirectory => cache.Directory;

    /// <summary>The first configured source, or null when no vendor key is in Vault.</summary>
    public IFundamentalsSource? Primary => Sources.FirstOrDefault(s => s.IsConfigured);

    public IFundamentalsSource? Find(string name) =>
        Sources.FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^[A-Z0-9][A-Z0-9.\\-]{0,14}$")]
    private static partial Regex SymbolPattern();

    public static bool IsValidSymbol(string symbol) => SymbolPattern().IsMatch(symbol);

    /// <summary>
    /// <paramref name="symbol"/>'s fundamentals from <paramref name="sourceName"/> (or the primary
    /// source): today's cached read when there is one (unless <paramref name="refresh"/>), else one
    /// vendor fetch, cached for the rest of the ET day (an unknown symbol is cached too, as an empty read).
    /// Errors are returned, not thrown, and are never cached.
    /// </summary>
    public async Task<FundamentalsLookup> GetAsync(string symbol, string? sourceName = null, bool refresh = false, CancellationToken ct = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        if (!IsValidSymbol(symbol)) return new(symbol, sourceName, null, false, $"Not a ticker symbol: {symbol}");

        IFundamentalsSource? source;
        if (sourceName is { Length: > 0 })
        {
            source = Find(sourceName);
            if (source is null) return new(symbol, sourceName, null, false, $"Unknown source '{sourceName}' (one of: {string.Join(", ", Sources.Select(s => s.Name))}).");
        }
        else
        {
            source = Primary;
            if (source is null)
                return new(symbol, null, null, false, "No fundamentals source is configured: " + string.Join("; ", Sources.Select(s => $"{s.Name}: {s.Status}")));
        }
        if (!source.IsConfigured) return new(symbol, source.Name, null, false, $"{source.DisplayName} is off: {source.Status}");

        var todayEt = TodayEt();
        if (!refresh && cache.TryGet(source.Name, symbol, todayEt) is { } cached)
            return new(symbol, source.Name, cached.IsEmpty && cached.Notes.Contains(FundamentalsCache.UnknownNote) ? null : cached, true, null);

        try
        {
            var snap = await source.FetchAsync(symbol, ct);
            cache.Put(snap ?? new FundamentalsSnapshot
            {
                Symbol = symbol, Source = source.Name, FetchedUtc = clock.GetUtcNow().UtcDateTime, Notes = [FundamentalsCache.UnknownNote],
            });
            return new(symbol, source.Name, snap, false, null);
        }
        catch (FundamentalsSourceException ex)
        {
            return new(symbol, source.Name, null, false, ex.Message);
        }
    }

    private DateOnly TodayEt() =>
        DateOnly.FromDateTime(global::IdiotProof.Evidence.Gappers.MinuteBarMath.ToEastern(clock.GetUtcNow().UtcDateTime));
}

/// <summary>
/// One JSON file per source and symbol (<c>%LOCALAPPDATA%\MindAttic\IdiotProof\Fundamentals\&lt;source&gt;\&lt;SYM&gt;.json</c>);
/// a file is fresh while its fetch falls on the current ET date, so each symbol costs at most one
/// vendor call per source per day. Holds vendor data only, never a key.
/// </summary>
public sealed class FundamentalsCache(string directory)
{
    internal const string UnknownNote = "symbol unknown to this source";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "IdiotProof", "Fundamentals");

    public string Directory { get; } = directory;

    private string PathFor(string source, string symbol) => Path.Combine(Directory, source, symbol + ".json");

    public FundamentalsSnapshot? TryGet(string source, string symbol, DateOnly todayEt)
    {
        var path = PathFor(source, symbol);
        try
        {
            if (!File.Exists(path)) return null;
            var snap = JsonSerializer.Deserialize<FundamentalsSnapshot>(File.ReadAllText(path), Json);
            if (snap is null) return null;
            var fetchedEt = DateOnly.FromDateTime(global::IdiotProof.Evidence.Gappers.MinuteBarMath.ToEastern(snap.FetchedUtc));
            return fetchedEt == todayEt ? snap : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public void Put(FundamentalsSnapshot snapshot)
    {
        try
        {
            var path = PathFor(snapshot.Source, snapshot.Symbol);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache write failure only costs a refetch.
        }
    }
}
