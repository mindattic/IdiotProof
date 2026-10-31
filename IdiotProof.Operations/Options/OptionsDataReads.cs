using System.Globalization;
using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;
using IdiotProof.Shared.Options;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Options;

/// <summary>
/// Which Alpaca options market data feed to read: <c>indicative</c> (free, the default) or
/// <c>opra</c> (needs the paid OPRA subscription).
/// </summary>
public enum OptionsFeed
{
    Indicative,
    Opra,
}

/// <summary>One account's slice of a scoped options read: the value, or why that account could not be read.</summary>
internal sealed record OptionsAccountRead<T>(string Account, T? Value, string? Error);

/// <summary>Black-Scholes sensitivities in the units Alpaca reports: theta per day, vega and rho per 1 point.</summary>
internal sealed record ModelGreeks(double Delta, double Gamma, double Theta, double Vega, double Rho);

/// <summary>Everything the local Black-Scholes model needs for one contract, with where spot came from.</summary>
internal sealed record ModelInputs(OptionContract Contract, OptionQuote Quote, decimal? Spot, string SpotSource, double Rate, double Years, int Dte);

/// <summary>
/// The read side of the options operations: per-account Trading API reads, the options market data
/// endpoints (snapshots, latest, bars, trades), the underlying's spot, OCC decoding, the risk-free
/// rate and the local Black-Scholes model that backs Greeks/IV whenever Alpaca omits them.
/// </summary>
internal static class OptionsDataReads
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Runs <paramref name="read"/> against each account in <paramref name="scope"/>. With scope All, an unreachable account becomes an error row instead of failing the call.</summary>
    public static async Task<List<OptionsAccountRead<T>>> PerAccountAsync<T>(OperationContext ctx, AccountScope scope,
        Func<AlpacaApi, CancellationToken, Task<T>> read)
    {
        var owner = ctx.RequireOwner();
        var factory = ctx.Get<AlpacaApiFactory>();
        BrokerMode[] modes = scope switch
        {
            AccountScope.Paper => [BrokerMode.Paper],
            AccountScope.Live => [BrokerMode.Live],
            _ => [BrokerMode.Paper, BrokerMode.Live],
        };
        var results = new List<OptionsAccountRead<T>>();
        foreach (var mode in modes)
        {
            var name = mode == BrokerMode.Paper ? "paper" : "live";
            try
            {
                await using var api = await factory.ForOwnerAsync(owner, mode, ctx.CancellationToken);
                results.Add(new(name, await read(api, ctx.CancellationToken), null));
            }
            catch (AlpacaAccessException ex) when (scope == AccountScope.All)
            {
                results.Add(new(name, default, ex.Message));
            }
            catch (AlpacaApiException ex) when (scope == AccountScope.All)
            {
                results.Add(new(name, default, ex.Message));
            }
        }
        return results;
    }

    /// <summary>A Market Data client (the owner's paper keys, else the Hub's). Also reaches the contracts catalog, which is the same for every account.</summary>
    public static Task<AlpacaApi> DataApiAsync(OperationContext ctx) =>
        ctx.Get<AlpacaApiFactory>().ForDataAsync(ctx.OwnerId, ctx.CancellationToken);

    /// <summary>Decodes an OCC symbol or throws a usage error that shows the layout.</summary>
    public static OptionContract RequireOcc(string occ) =>
        OptionContract.ParseOcc(occ) ?? throw OperationException.Usage(
            $"'{occ}' is not an OCC option symbol: ROOT + YYMMDD + C|P + strike x 1000 in 8 digits, e.g. AAPL261120C00250000.");

    public static string FeedName(OptionsFeed feed) => feed == OptionsFeed.Opra ? "opra" : "indicative";

    /// <summary>A query dictionary from pairs; null values are dropped by <see cref="AlpacaApi.Query"/>.</summary>
    public static Dictionary<string, string?> Query(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    /// <summary><c>GET /v1beta1/options/snapshots?symbols=occ</c>: the contract's snapshot, or null when Alpaca has none.</summary>
    public static async Task<JsonElement?> SnapshotAsync(AlpacaApi api, string occ, OptionsFeed feed, CancellationToken ct)
    {
        var root = await api.GetDataAsync("/v1beta1/options/snapshots", Query(("symbols", occ), ("feed", FeedName(feed))), ct);
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("snapshots", out var all)
            && all.ValueKind == JsonValueKind.Object && all.TryGetProperty(occ, out var snap) ? snap : null;
    }

    /// <summary>The snapshot or a Failed result naming the contract.</summary>
    public static async Task<JsonElement> RequireSnapshotAsync(AlpacaApi api, string occ, OptionsFeed feed, CancellationToken ct) =>
        await SnapshotAsync(api, occ, feed, ct)
        ?? throw OperationException.Failed($"Alpaca has no {FeedName(feed)} snapshot for {occ} (expired, never traded, or not a listed contract).");

    /// <summary>
    /// Every snapshot in an underlying's chain for one expiration
    /// (<c>GET /v1beta1/options/snapshots/{underlying}</c>, paged), keyed by OCC symbol.
    /// </summary>
    public static async Task<Dictionary<string, JsonElement>> ChainSnapshotsAsync(AlpacaApi api, string underlying, DateOnly expiration,
        OptionsFeed feed, CancellationToken ct)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var q = Query(("feed", FeedName(feed)), ("expiration_date", expiration.ToString("yyyy-MM-dd", Inv)), ("limit", "1000"));
        for (var page = 0; page < 20; page++)
        {
            var root = await api.GetDataAsync($"/v1beta1/options/snapshots/{Uri.EscapeDataString(underlying)}", q, ct);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("snapshots", out var snaps) && snaps.ValueKind == JsonValueKind.Object)
                foreach (var p in snaps.EnumerateObject()) result[p.Name] = p.Value;
            var token = NextPageToken(root);
            if (token is null) break;
            q["page_token"] = token;
        }
        return result;
    }

    /// <summary>
    /// <c>GET /v2/options/contracts</c>, following <c>next_page_token</c>. The caller sends the
    /// expiration window: Alpaca silently defaults <c>expiration_date_lte</c> to one week out.
    /// </summary>
    public static async Task<List<JsonElement>> ContractsAsync(AlpacaApi api, Dictionary<string, string?> query, int max, CancellationToken ct)
    {
        var list = new List<JsonElement>();
        var q = new Dictionary<string, string?>(query) { ["limit"] = Math.Clamp(max, 1, 10_000).ToString(Inv) };
        for (var page = 0; page < 20 && list.Count < max; page++)
        {
            var root = await api.GetAsync("/v2/options/contracts", q, ct);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("option_contracts", out var arr) && arr.ValueKind == JsonValueKind.Array)
                list.AddRange(arr.EnumerateArray());
            var token = NextPageToken(root);
            if (token is null) break;
            q["page_token"] = token;
        }
        return list.Count > max ? list[..max] : list;
    }

    /// <summary>The underlying's last trade price from the stock data API, or null when Alpaca has none.</summary>
    public static async Task<decimal?> SpotAsync(AlpacaApi api, string underlying, CancellationToken ct)
    {
        try
        {
            var root = await api.GetDataAsync($"/v2/stocks/{Uri.EscapeDataString(underlying)}/trades/latest", null, ct);
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("trade", out var t) ? Dec(t, "p") : null;
        }
        catch (AlpacaApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// The annual risk-free rate as a fraction: <paramref name="overridePercent"/> (percent units) when
    /// given, else the Options page's saved setting, else <see cref="OptionsTradingService.DefaultRiskFreeRate"/>.
    /// </summary>
    public static async Task<double> RiskFreeRateAsync(OperationContext ctx, decimal? overridePercent)
    {
        if (overridePercent is { } p) return (double)p / 100.0;
        var repo = ctx.Services.GetService<SettingsRepository>();
        if (repo is not null)
        {
            try
            {
                if (await repo.GetDecimalAsync(OptionsTradingService.RiskFreeRateKey, ctx.CancellationToken) is { } saved) return (double)saved;
            }
            catch (Exception)
            {
                // No database reachable: the documented default below is the honest fallback.
            }
        }
        return (double)OptionsTradingService.DefaultRiskFreeRate;
    }

    /// <summary>
    /// Loads a contract's snapshot, the underlying's spot (unless <paramref name="spotOverride"/>) and
    /// the risk-free rate (unless <paramref name="ratePercent"/>), for the Greeks / value / IV reads.
    /// </summary>
    public static async Task<ModelInputs> LoadModelInputsAsync(OperationContext ctx, string occ, OptionsFeed feed,
        decimal? spotOverride, decimal? ratePercent)
    {
        var contract = RequireOcc(occ);
        if (spotOverride is <= 0) throw OperationException.Usage("--spot must be greater than 0.");
        await using var api = await DataApiAsync(ctx);
        var snap = await RequireSnapshotAsync(api, contract.OccSymbol, feed, ctx.CancellationToken);
        var quote = ParseSnapshot(contract.OccSymbol, snap);
        var spot = spotOverride ?? await SpotAsync(api, contract.UnderlyingSymbol, ctx.CancellationToken);
        var rate = await RiskFreeRateAsync(ctx, ratePercent);
        return new ModelInputs(contract, quote, spot, spotOverride is null ? "Alpaca last trade" : "--spot", rate,
            Years(contract, ctx.UtcNow), DaysToExpiry(contract, ctx.UtcNow));
    }

    /// <summary>The snapshot as an <see cref="OptionQuote"/>: bid/ask, last trade, and Alpaca's IV/Greeks when it sent them.</summary>
    public static OptionQuote ParseSnapshot(string occ, JsonElement snap)
    {
        decimal bid = 0m, ask = 0m;
        decimal? last = null;
        var ts = DateTime.MinValue;
        if (Obj(snap, "latestQuote") is { } q)
        {
            bid = Dec(q, "bp") ?? 0m;
            ask = Dec(q, "ap") ?? 0m;
            ts = Time(q, "t") ?? ts;
        }
        if (Obj(snap, "latestTrade") is { } tr) last = Dec(tr, "p");
        OptionGreeks? greeks = Obj(snap, "greeks") is { } g
            ? new OptionGreeks(Dec(g, "delta") ?? 0m, Dec(g, "gamma") ?? 0m, Dec(g, "theta") ?? 0m, Dec(g, "vega") ?? 0m, Dec(g, "rho") ?? 0m)
            : null;
        return new OptionQuote(occ, bid, ask, last, Dec(snap, "impliedVolatility"), greeks, ts);
    }

    /// <summary>Calendar days from today (ET) to expiration; 0 on expiration day.</summary>
    public static int DaysToExpiry(OptionContract c, DateTime nowUtc) =>
        c.Expiration.DayNumber - DateOnly.FromDateTime(EasternClock.ToEastern(nowUtc)).DayNumber;

    /// <summary>Years to the 4:00 PM ET expiry, as the model takes it.</summary>
    public static double Years(OptionContract c, DateTime nowUtc) => BlackScholesCalculator.YearsUntil(c.Expiration, nowUtc);

    public static decimal Intrinsic(OptionContract c, decimal spot) =>
        c.Right == OptionRight.Call ? Math.Max(0m, spot - c.Strike) : Math.Max(0m, c.Strike - spot);

    /// <summary>ITM / ATM (within half a percent of the strike) / OTM.</summary>
    public static string Moneyness(OptionContract c, decimal spot)
    {
        if (c.Strike > 0 && Math.Abs(spot - c.Strike) / c.Strike <= 0.005m) return "ATM";
        return Intrinsic(c, spot) > 0 ? "ITM" : "OTM";
    }

    /// <summary>Black-Scholes Greeks (q = 0, European) in Alpaca's units.</summary>
    public static ModelGreeks Greeks(double s, double k, double t, double r, double sigma, OptionRight right)
    {
        var delta = BlackScholesCalculator.Delta(s, k, t, r, sigma, right);
        if (s <= 0 || k <= 0 || t <= 0 || sigma <= 0) return new ModelGreeks(delta, 0, 0, 0, 0);
        var sqrtT = Math.Sqrt(t);
        var d1 = (Math.Log(s / k) + (r + sigma * sigma / 2) * t) / (sigma * sqrtT);
        var d2 = d1 - sigma * sqrtT;
        var pdf = BlackScholesCalculator.NormalPdf(d1);
        var discountedStrike = k * Math.Exp(-r * t);
        var gamma = pdf / (s * sigma * sqrtT);
        var decay = -(s * pdf * sigma) / (2 * sqrtT);
        var theta = right == OptionRight.Call
            ? decay - r * discountedStrike * BlackScholesCalculator.NormalCdf(d2)
            : decay + r * discountedStrike * BlackScholesCalculator.NormalCdf(-d2);
        var rho = right == OptionRight.Call
            ? k * t * Math.Exp(-r * t) * BlackScholesCalculator.NormalCdf(d2)
            : -k * t * Math.Exp(-r * t) * BlackScholesCalculator.NormalCdf(-d2);
        return new ModelGreeks(delta, gamma, theta / 365.0, BlackScholesCalculator.Vega(s, k, t, r, sigma) / 100.0, rho / 100.0);
    }

    /// <summary>Solves IV for a premium, or null when the price is outside what any volatility can produce.</summary>
    public static double? SolveIv(OptionContract c, decimal spot, decimal premium, double years, double rate) =>
        premium <= 0 || spot <= 0 ? null
        : BlackScholesCalculator.ImpliedVolatility((double)spot, (double)c.Strike, years, rate, (double)premium, c.Right);

    // ---------------------------------------------------------------- JSON + formatting

    /// <summary>Alpaca's Trading API sends numbers as strings, the Data API as numbers. Accepts both.</summary>
    public static decimal? Dec(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDecimal(out var d) ? d : (decimal)p.GetDouble(),
            JsonValueKind.String => decimal.TryParse(p.GetString(), NumberStyles.Any, Inv, out var s) ? s : null,
            _ => null,
        };
    }

    public static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null
            ? p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString()
            : null;

    public static bool? Bool(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean() : null;

    public static JsonElement? Obj(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object ? p : null;

    /// <summary>An RFC 3339 timestamp as a UTC <see cref="DateTime"/>.</summary>
    public static DateTime? Time(JsonElement el, string name) =>
        Str(el, name) is { } s && DateTimeOffset.TryParse(s, Inv, DateTimeStyles.AssumeUniversal, out var t) ? t.UtcDateTime : null;

    public static string? NextPageToken(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_page_token", out var t) && t.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(t.GetString()) ? t.GetString() : null;

    public static string Money(decimal? v) => v is { } m ? Fmt.Money(m) : "-";

    public static string Iv(decimal? v) => v is { } x ? Fmt.Percent(x, 1) : "-";

    public static string Iv(double? v) => v is { } x ? Fmt.Percent(x, 1) : "-";

    public static string Greek(double v, int decimals = 4) => v.ToString("0." + new string('0', decimals), Inv);

    public static string Greek(decimal? v, int decimals = 4) => v is { } x ? Greek((double)x, decimals) : "-";

    public static string Strike(decimal strike) => strike.ToString("0.##", Inv);

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    public static string Time(DateTime? utc) => utc is { } t && t > DateTime.MinValue ? Fmt.Et(t) : "-";
}
