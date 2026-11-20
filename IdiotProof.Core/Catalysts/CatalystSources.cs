using System.Globalization;
using IdiotProof.Core.Services;

namespace IdiotProof.Core.Catalysts;

public enum CatalystSourceKind
{
    /// <summary>A press-release wire: items become catalyst events (or sightings of an existing story).</summary>
    Wire,

    /// <summary>A social-attention source: readings become <see cref="Data.SocialSignal"/> rows, never catalysts.</summary>
    Social,
}

/// <summary>
/// One extra catalyst-intake source (IP-T602) and its switches: <c>catalyst.intake.sources.&lt;name&gt;</c> turns it on or
/// off, <c>catalyst.intake.sources.&lt;name&gt;.url</c> replaces its feed URL(s) (comma-separated). A source with a
/// <see cref="VaultEntries"/> list stays off until every entry is in the Vault Tokens bucket.
/// </summary>
public sealed record CatalystSourceInfo(
    string Name,
    string DisplayName,
    CatalystSourceKind Kind,
    bool DefaultOn,
    IReadOnlyList<string> DefaultUrls,
    TimeSpan Cadence,
    IReadOnlyList<string> VaultEntries,
    string Note)
{
    public string EnabledKey => $"{CatalystSources.KeyPrefix}{Name}";
    public string UrlKey => $"{CatalystSources.KeyPrefix}{Name}.url";
}

/// <summary>The registry of the IP-T602 sources: five press-release wires and two social sources.</summary>
public static class CatalystSources
{
    public const string KeyPrefix = "catalyst.intake.sources.";

    public const string PrNewswire = "prnewswire";
    public const string BusinessWire = "businesswire";
    public const string GlobeNewswire = "globenewswire";
    public const string Accesswire = "accesswire";
    public const string Newsfile = "newsfile";
    public const string StockTwits = "stocktwits";
    public const string Reddit = "reddit";

    /// <summary>Subreddits read for mention counts unless <see cref="RedditSubredditsKey"/> names others.</summary>
    public const string RedditSubredditsKey = KeyPrefix + "reddit.subreddits";
    public static readonly string[] DefaultSubreddits = ["wallstreetbets", "pennystocks", "smallstreetbets"];

    /// <summary>Reddit app-only OAuth (client credentials): its public JSON answers 403 to servers.</summary>
    public const string RedditClientIdEntry = "reddit-client-id";
    public const string RedditClientSecretEntry = "reddit-client-secret";

    public static readonly IReadOnlyList<CatalystSourceInfo> All =
    [
        new(PrNewswire, "PR Newswire", CatalystSourceKind.Wire, true,
            ["https://www.prnewswire.com/rss/news-releases-list.rss"], TimeSpan.FromSeconds(60), [],
            "All news releases RSS (verified 2026-11-19); only the newest 20 items per fetch, so a burst over 20 a minute can skip some. Tickers from \"(NASDAQ: ABCD)\" in the title/summary."),
        new(BusinessWire, "Business Wire", CatalystSourceKind.Wire, true,
            ["https://feed.businesswire.com/rss/home/?rss=G1QFDERJXkJeEF9YXA==", "https://feed.businesswire.com/rss/home/?rss=G1QFDERJXkJeEFtRWA=="], TimeSpan.FromSeconds(60), [],
            "Earnings and Merger/Acquisition category feeds (verified 2026-11-19). No all-news feed could be verified; add category feeds via the .url setting. Tickers from \"(NYSE: ABC)\" in the summary."),
        new(GlobeNewswire, "GlobeNewswire", CatalystSourceKind.Wire, false,
            ["https://www.globenewswire.com/RssFeed/orgclass/1/feedTitle/GlobeNewswire%20-%20News%20about%20Public%20Companies"], TimeSpan.FromSeconds(60), [],
            "OFF by default: the public-companies RSS answered 403 (Akamai) from this host on 2026-11-19. Turn it on to try, or set a working URL in the .url setting."),
        new(Accesswire, "ACCESS Newswire", CatalystSourceKind.Wire, false,
            [], TimeSpan.FromSeconds(60), [],
            "OFF by default: no public press-release feed could be verified (accessnewswire.com/feed/rss2 is its marketing blog). Set a feed URL in the .url setting, then turn it on."),
        new(Newsfile, "Newsfile (TMX)", CatalystSourceKind.Wire, true,
            ["https://feeds.newsfilecorp.com/global/Last25Stories"], TimeSpan.FromSeconds(60), [],
            "Last 25 stories RSS (verified 2026-11-19), truncated summaries. Canadian listings carry their OTC ticker, which is the one kept."),
        new(StockTwits, "StockTwits", CatalystSourceKind.Social, false,
            ["https://api.stocktwits.com/api/2/trending/symbols.json"], TimeSpan.FromMinutes(5), [],
            "Public API, no key (verified 2026-11-19): trending symbols every 5 min, and per-symbol message counts (last 60 min, capped at the 30 the stream returns) for trending symbols and today's catalyst symbols, at most 15 a pass. Unauthenticated limit ~200 calls/hour."),
        new(Reddit, "Reddit", CatalystSourceKind.Social, false,
            [], TimeSpan.FromMinutes(2), [RedditClientIdEntry, RedditClientSecretEntry],
            "Mention counts ($TICKER cashtags and known tickers in titles/bodies of the newest 100 posts, last 60 min) per subreddit. Needs a Reddit script app: its public JSON answers 403 to servers, so this uses app-only OAuth."),
    ];

    public static CatalystSourceInfo? Find(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for a press-release wire source name (the catalyst event's <c>Source</c>).</summary>
    public static bool IsWire(string? source) =>
        source is not null && All.Any(s => s.Kind == CatalystSourceKind.Wire && string.Equals(s.Name, source, StringComparison.OrdinalIgnoreCase));

    /// <summary>The SettingsKv rows for <see cref="SettingsMetadata"/>.</summary>
    public static IEnumerable<SettingDefinition> SettingDefinitions()
    {
        foreach (var s in All)
        {
            yield return new SettingDefinition(s.EnabledKey, SettingKind.Bool,
                $"Catalyst intake source (IP-T602): {s.DisplayName} ({(s.Kind == CatalystSourceKind.Wire ? "press-release wire, items become catalyst events with first-seen sightings" : "social attention, readings become SocialSignals")}). Runs only while catalyst.intake.enabled is on and this Hub leads. {s.Note}",
                s.DefaultOn ? "true" : "false");
            yield return new SettingDefinition(s.UrlKey, SettingKind.Text,
                $"Catalyst intake source {s.DisplayName}: feed URL(s) replacing the built-in ones, comma-separated. Unset = {(s.DefaultUrls.Count == 0 ? "none (the source has no verified feed)" : string.Join(", ", s.DefaultUrls))}.");
        }
        yield return new SettingDefinition(RedditSubredditsKey, SettingKind.Text,
            "Catalyst intake source Reddit: subreddits read for mention counts, comma-separated (no r/).", string.Join(",", DefaultSubreddits));
    }
}

/// <summary>One source's switches as read now.</summary>
public sealed record CatalystSourceConfig(CatalystSourceInfo Info, bool Enabled, IReadOnlyList<string> Urls, bool UrlOverridden);

/// <summary>Reads every source's switches (cached briefly, like <see cref="CatalystIntakeSettings"/>).</summary>
public sealed class CatalystSourceSettings(SettingsRepository settings, TimeProvider time)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private readonly Lock gate = new();
    private (IReadOnlyDictionary<string, CatalystSourceConfig> Sources, IReadOnlyList<string> Subreddits)? cached;
    private DateTime cachedAtUtc;

    public async Task<IReadOnlyDictionary<string, CatalystSourceConfig>> ReadAsync(CancellationToken ct = default) =>
        (await ReadAllAsync(ct)).Sources;

    public async Task<IReadOnlyList<string>> SubredditsAsync(CancellationToken ct = default) =>
        (await ReadAllAsync(ct)).Subreddits;

    private async Task<(IReadOnlyDictionary<string, CatalystSourceConfig> Sources, IReadOnlyList<string> Subreddits)> ReadAllAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (gate)
        {
            if (cached is { } c && now - cachedAtUtc < Ttl) return c;
        }
        var all = await settings.GetAllAsync(ct);
        string? Get(string key) => all.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var map = new Dictionary<string, CatalystSourceConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in CatalystSources.All)
        {
            var on = Get(s.EnabledKey) is { } raw ? ParseBool(raw) ?? s.DefaultOn : s.DefaultOn;
            var urls = Get(s.UrlKey) is { } u ? Split(u) : null;
            map[s.Name] = new CatalystSourceConfig(s, on, urls ?? s.DefaultUrls, urls is not null);
        }
        var subs = Get(CatalystSources.RedditSubredditsKey) is { } subText
            ? Split(subText).Select(x => x.TrimStart('/').Replace("r/", "", StringComparison.OrdinalIgnoreCase)).Where(x => x.Length > 0).ToList()
            : CatalystSources.DefaultSubreddits.ToList();
        var fresh = ((IReadOnlyDictionary<string, CatalystSourceConfig>)map, (IReadOnlyList<string>)subs);
        lock (gate)
        {
            cached = fresh;
            cachedAtUtc = now;
        }
        return fresh;
    }

    public void Invalidate()
    {
        lock (gate) cached = null;
    }

    private static List<string> Split(string text) =>
        text.Split([',', ' ', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool? ParseBool(string raw) => raw.ToLower(CultureInfo.InvariantCulture) switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => null,
    };
}
