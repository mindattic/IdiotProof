using System.Globalization;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Engine.Settings;
using IdiotProof.Models;

namespace IdiotProof.Core.Monitoring.FadeShort;

/// <summary>
/// Everything the borrow pilot may ask Alpaca, all of it GETs on an allow-list (IP-T480): movers, minute and
/// daily bars, one asset, and the advisory locate quote. There is no method that places, reserves, replaces or
/// cancels anything, and the implementation refuses any path outside <see cref="FadeShortReadOnlyEndpoints"/>.
/// </summary>
public interface IFadeShortBorrowReader
{
    /// <summary>A read-only session over the owner's PAPER route; throws when the route is not paper.</summary>
    Task<IFadeShortBorrowSession> OpenAsync(Guid ownerUserId, CancellationToken ct);
}

public interface IFadeShortBorrowSession : IAsyncDisposable
{
    Task<IReadOnlyList<(string Symbol, double Percent, double Price)>> MoversAsync(int top, CancellationToken ct);
    Task<IReadOnlyList<Candle>> MinuteBarsAsync(string symbol, DateTime fromUtc, DateTime toUtc, string feed, CancellationToken ct);
    Task<IReadOnlyList<Candle>> DailyBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, string feed, CancellationToken ct);
    Task<JsonElement> AssetAsync(string symbol, CancellationToken ct);
    /// <summary><c>GET /v1/locates/quotes?symbols=...</c>: advisory, reserves no shares, charges nothing. Never <c>POST /v1/locates</c>.</summary>
    Task<JsonElement> LocateQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct);
}

/// <summary>The only Alpaca paths the pilot may call, all with GET (IP-T480's no-order guard).</summary>
public static class FadeShortReadOnlyEndpoints
{
    public const string LocateQuotes = "/v1/locates/quotes";
    public const string Movers = "/v1beta1/screener/stocks/movers";

    /// <summary>Trading API: one asset, or the locate quote. Not orders, positions, account mutations or <c>/v1/locates</c> itself.</summary>
    public static bool IsAllowedTrading(string path) =>
        path == LocateQuotes || (path.StartsWith("/v2/assets/", StringComparison.Ordinal) && path.Count(c => c == '/') == 3);

    /// <summary>Market Data API: the movers screen and one symbol's bars.</summary>
    public static bool IsAllowedData(string path) =>
        path == Movers || (path.StartsWith("/v2/stocks/", StringComparison.Ordinal) && path.EndsWith("/bars", StringComparison.Ordinal) && path.Count(c => c == '/') == 4);

    public static void AssertTrading(string path)
    {
        if (!IsAllowedTrading(path)) throw new InvalidOperationException($"Fade-short pilot refused Trading API path '{path}': the pilot is read-only (IP-T480).");
    }

    public static void AssertData(string path)
    {
        if (!IsAllowedData(path)) throw new InvalidOperationException($"Fade-short pilot refused Market Data path '{path}': the pilot is read-only (IP-T480).");
    }
}

/// <summary>
/// The Alpaca implementation over the owner's paper keys (<see cref="AlpacaApiFactory.ForOwnerAsync"/> with
/// <see cref="BrokerMode.Paper"/>). Every call goes through a GET helper that checks the allow-list first.
/// </summary>
public sealed class AlpacaFadeShortBorrowReader(AlpacaApiFactory factory) : IFadeShortBorrowReader
{
    public async Task<IFadeShortBorrowSession> OpenAsync(Guid ownerUserId, CancellationToken ct)
    {
        var api = await factory.ForOwnerAsync(ownerUserId, BrokerMode.Paper, ct);
        if (!api.IsPaper)
        {
            await api.DisposeAsync();
            throw new InvalidOperationException("Fade-short pilot: the owner's paper route resolved to a live client; refusing.");
        }
        return new Session(api);
    }

    private sealed class Session(AlpacaApi api) : IFadeShortBorrowSession
    {
        public async Task<IReadOnlyList<(string Symbol, double Percent, double Price)>> MoversAsync(int top, CancellationToken ct)
        {
            var root = await Data(FadeShortReadOnlyEndpoints.Movers, new Dictionary<string, string?> { ["top"] = Math.Clamp(top, 1, 50).ToString(CultureInfo.InvariantCulture) }, ct);
            return StrategyScanner.ParseGainers(root);
        }

        public async Task<IReadOnlyList<Candle>> MinuteBarsAsync(string symbol, DateTime fromUtc, DateTime toUtc, string feed, CancellationToken ct) =>
            await Bars(symbol, "1Min", fromUtc.ToString("O", CultureInfo.InvariantCulture), toUtc.ToString("O", CultureInfo.InvariantCulture), feed, TimeSpan.FromMinutes(1), ct);

        public async Task<IReadOnlyList<Candle>> DailyBarsAsync(string symbol, DateOnly fromEt, DateOnly toEt, string feed, CancellationToken ct) =>
            await Bars(symbol, "1Day", fromEt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), toEt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), feed, TimeSpan.FromDays(1), ct);

        public Task<JsonElement> AssetAsync(string symbol, CancellationToken ct) =>
            Trading($"/v2/assets/{Uri.EscapeDataString(symbol)}", null, ct);

        public Task<JsonElement> LocateQuotesAsync(IReadOnlyList<string> symbols, CancellationToken ct) =>
            Trading(FadeShortReadOnlyEndpoints.LocateQuotes, new Dictionary<string, string?> { ["symbols"] = string.Join(",", symbols) }, ct);

        private async Task<List<Candle>> Bars(string symbol, string timeframe, string start, string end, string feed, TimeSpan span, CancellationToken ct)
        {
            var path = $"/v2/stocks/{Uri.EscapeDataString(symbol)}/bars";
            FadeShortReadOnlyEndpoints.AssertData(path);
            var query = new Dictionary<string, string?>
            {
                ["timeframe"] = timeframe, ["start"] = start, ["end"] = end,
                ["adjustment"] = "all", ["feed"] = feed, ["limit"] = "10000",
            };
            var list = new List<Candle>();
            await foreach (var b in api.GetDataPagedAsync(path, "bars", query, maxPages: 10, ct: ct))
                if (FadeShortPilotMath.ParseBar(symbol, b, span) is { } c) list.Add(c);
            return list.OrderBy(c => c.StartUtc).ToList();
        }

        private Task<JsonElement> Trading(string path, IReadOnlyDictionary<string, string?>? query, CancellationToken ct)
        {
            FadeShortReadOnlyEndpoints.AssertTrading(path);
            return api.GetAsync(path, query, ct);
        }

        private Task<JsonElement> Data(string path, IReadOnlyDictionary<string, string?>? query, CancellationToken ct)
        {
            FadeShortReadOnlyEndpoints.AssertData(path);
            return api.GetDataAsync(path, query, ct);
        }

        public ValueTask DisposeAsync() => api.DisposeAsync();
    }
}
