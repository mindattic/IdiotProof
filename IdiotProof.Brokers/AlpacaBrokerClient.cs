using System.Net.Http.Json;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Alpaca broker implementation using REST API.
/// </summary>
public sealed class AlpacaBrokerClient : IBrokerClient, IAsyncDisposable
{
    private const string DataBaseUri = "https://data.alpaca.markets";

    /// <summary>Trading API (paper-api / api host): orders, positions, account, contract catalog.</summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Market-data API (data host) — a DIFFERENT host from trading. Used only for option
    /// snapshots (quotes + server-side Greeks/IV). Same credentials.
    /// </summary>
    private readonly HttpClient dataClient;

    private bool connected;

    public BrokerType BrokerType => BrokerType.Alpaca;

    /// <summary>Paper vs live — fixed at construction by the endpoint chosen.</summary>
    public bool IsPaper { get; }

    public bool IsConnected => connected;

    /// <summary>This client implements the options endpoints; account approval is a separate question.</summary>
    public bool SupportsOptions => true;

    /// <summary>
    /// Options snapshot feed: <c>indicative</c> (free, delayed-ish indicative quotes) or
    /// <c>opra</c> (requires the paid options data subscription). Defaults to indicative
    /// so a fresh account gets numbers; flip to opra once entitled.
    /// </summary>
    public string OptionsDataFeed { get; set; } = "indicative";

    public AlpacaBrokerClient(string apiKeyId, string apiSecretKey, bool isPaper = true)
    {
        IsPaper = isPaper;
        httpClient = new HttpClient { BaseAddress = new Uri(TradingBaseUri(isPaper)), Timeout = TimeSpan.FromSeconds(30) };
        dataClient = new HttpClient { BaseAddress = new Uri(DataBaseUri), Timeout = TimeSpan.FromSeconds(30) };

        if (!string.IsNullOrWhiteSpace(apiKeyId) && !string.IsNullOrWhiteSpace(apiSecretKey))
        {
            foreach (var client in new[] { httpClient, dataClient })
            {
                client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKeyId);
                client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey);
            }
        }
    }

    /// <summary>
    /// Test seam: route both the trading and data clients through a caller-supplied handler
    /// so request payloads and response parsing can be asserted without the network.
    /// </summary>
    internal AlpacaBrokerClient(HttpMessageHandler handler, bool isPaper = true)
    {
        IsPaper = isPaper;
        httpClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(TradingBaseUri(isPaper)) };
        dataClient = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(DataBaseUri) };
    }

    private static string TradingBaseUri(bool isPaper) =>
        isPaper ? "https://paper-api.alpaca.markets" : "https://api.alpaca.markets";

    /// <summary>
    /// OAuth (Connect API) construction — authenticates with an
    /// <c>Authorization: Bearer &lt;token&gt;</c> header instead of the key/secret
    /// pair (IP-A26), for account-linked users. DORMANT by design: nothing in the
    /// routing path builds a client this way yet — <c>UserBrokerResolver</c> still
    /// uses the key/secret ctor. Enabling it is a gated step (register an Alpaca
    /// OAuth app, then paper-test) per <see cref="AlpacaOAuthClient"/>.
    /// </summary>
    public static AlpacaBrokerClient FromOAuthToken(string accessToken, bool isPaper = true) =>
        new(accessToken, isPaper, oauth: true);

    private AlpacaBrokerClient(string accessToken, bool isPaper, bool oauth)
    {
        IsPaper = isPaper;
        httpClient = new HttpClient { BaseAddress = new Uri(TradingBaseUri(isPaper)), Timeout = TimeSpan.FromSeconds(30) };
        dataClient = new HttpClient { BaseAddress = new Uri(DataBaseUri), Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            foreach (var client in new[] { httpClient, dataClient })
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        connected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        connected = false;
        return Task.CompletedTask;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return new OrderResult { IsSuccess = false, Message = "Symbol required." };

        if (request.AssetClass == AssetClass.Option)
        {
            // Options orders ride the same /v2/orders endpoint but with stricter rules:
            // OCC symbol, whole contracts only (no notional), DAY time-in-force, and no
            // extended-hours session. Reject locally with a plain message rather than
            // letting Alpaca 422.
            if (request.Option is null)
                return new OrderResult { IsSuccess = false, Message = "Option order requires the contract (OrderRequest.Option)." };
            if (request.Notional is { } && request.Quantity <= 0)
                return new OrderResult { IsSuccess = false, Message = "Options are sized in whole contracts — notional (dollar) sizing is not available." };
            if (request.Quantity <= 0)
                return new OrderResult { IsSuccess = false, Message = "Option order requires a contract count of at least 1." };
            if (request.ExtendedHours)
                return new OrderResult { IsSuccess = false, Message = "Options do not trade in extended hours." };
            if (!request.TimeInForce.Equals("DAY", StringComparison.OrdinalIgnoreCase))
                return new OrderResult { IsSuccess = false, Message = "Options orders must use DAY time-in-force." };
            if (request.Type is not (OrderType.Market or OrderType.Limit))
                return new OrderResult { IsSuccess = false, Message = "Options orders support Market or Limit only." };

            var optionPayload = new
            {
                symbol          = request.Option.OccSymbol,
                qty             = request.Quantity,
                side            = request.Side == OrderSide.Buy ? "buy" : "sell",
                type            = request.Type == OrderType.Limit ? "limit" : "market",
                time_in_force   = "day",
                limit_price     = request.Type == OrderType.Limit ? request.LimitPrice : null,
                position_intent = request.PositionIntent,
            };
            return await PostOrderAsync(optionPayload, ct).ConfigureAwait(false);
        }

        // Either qty (shares) or notional (dollars) must be set — Alpaca
        // accepts exactly one. Quantity wins when both are populated;
        // empty-both is rejected so we don't accidentally place a $0 order.
        var hasShares   = request.Quantity > 0;
        var hasNotional = request.Notional is { } n && n > 0m;
        if (!hasShares && !hasNotional)
        {
            return new OrderResult { IsSuccess = false, Message = "Order requires either Quantity (shares) or Notional (dollars)." };
        }

        var side = request.Side == OrderSide.Buy ? "buy" : "sell";

        // Map every OrderType to its Alpaca string. The previous code collapsed
        // everything that wasn't Market to "limit", which silently turned a
        // protective Stop / StopLimit / TrailingStop into a plain limit order with
        // no stop price — a risk stop the strategy thinks exists but the broker
        // never placed.
        var type = request.Type switch
        {
            OrderType.Market       => "market",
            OrderType.Limit        => "limit",
            OrderType.Stop         => "stop",
            OrderType.StopLimit    => "stop_limit",
            OrderType.TrailingStop => "trailing_stop",
            _                      => "market"
        };

        // Only the price fields the chosen type actually uses are emitted; the rest
        // stay null so System.Text.Json omits them (Alpaca 422s on a stray price).
        var limitPrice = request.Type is OrderType.Limit or OrderType.StopLimit ? request.LimitPrice : null;
        var stopPrice  = request.Type is OrderType.Stop or OrderType.StopLimit ? request.StopPrice : null;
        var trailPct   = request.Type == OrderType.TrailingStop ? request.TrailPercent : null;

        // Alpaca hard-requires extended-hours orders to be limit + DAY; reject
        // locally with a clear message instead of letting the API 422 (or worse,
        // letting a market order silently queue for the 9:30 bell).
        if (request.ExtendedHours && (request.Type != OrderType.Limit || !request.TimeInForce.Equals("DAY", StringComparison.OrdinalIgnoreCase)))
        {
            return new OrderResult
            {
                IsSuccess = false,
                Message = "Extended-hours orders must be Limit type with DAY time-in-force (Alpaca requirement)."
            };
        }

        // Alpaca's /v2/orders accepts qty XOR notional. We send only the field
        // that's actually set so the API doesn't 422 on the "both" case.
        object payload = hasShares
            ? new
            {
                symbol          = request.Symbol.ToUpperInvariant(),
                qty             = request.Quantity,
                side,
                type,
                time_in_force   = request.TimeInForce.ToLowerInvariant(),
                limit_price     = limitPrice,
                stop_price      = stopPrice,
                trail_percent   = trailPct,
                extended_hours  = request.ExtendedHours,
                position_intent = request.PositionIntent,
            }
            : new
            {
                symbol          = request.Symbol.ToUpperInvariant(),
                notional        = request.Notional!.Value,
                side,
                type,
                time_in_force   = request.TimeInForce.ToLowerInvariant(),
                limit_price     = limitPrice,
                stop_price      = stopPrice,
                trail_percent   = trailPct,
                extended_hours  = request.ExtendedHours,
                position_intent = request.PositionIntent,
            };

        return await PostOrderAsync(payload, ct).ConfigureAwait(false);
    }

    private async Task<OrderResult> PostOrderAsync(object payload, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync("/v2/orders", payload, ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new OrderResult
            {
                IsSuccess = false,
                Message = $"HTTP {(int)response.StatusCode}: {content}"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var id = doc.RootElement.TryGetProperty("id", out var idProp)
                ? idProp.GetString() ?? string.Empty
                : string.Empty;
            return new OrderResult { BrokerOrderId = id, IsSuccess = true, Message = "Alpaca order placed." };
        }
        catch (Exception ex)
        {
            // The HTTP call returned 2xx — Alpaca accepted the order — but we couldn't
            // extract the id. Don't claim failure (caller would retry → duplicate); fall
            // back to the request-id header so reconciliation can find it.
            var requestId = response.Headers.TryGetValues("Apca-Request-Id", out var ids)
                ? string.Join(",", ids)
                : "";
            Console.Error.WriteLine($"[Alpaca] Order placed but response parse failed. ApcaRequestId={requestId} body={content} error={ex.Message}");
            return new OrderResult
            {
                BrokerOrderId = requestId,
                IsSuccess = true,
                Message = $"Order placed but response parse failed (ApcaRequestId={requestId}). Reconcile via /v2/orders before retrying."
            };
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        using var response = await httpClient.DeleteAsync($"/v2/orders/{orderId}", ct).ConfigureAwait(false);

        if ((int)response.StatusCode == 204)
        {
            return new OrderResult { BrokerOrderId = orderId, IsSuccess = true, Message = "Order cancelled." };
        }

        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new OrderResult
        {
            BrokerOrderId = orderId,
            IsSuccess = false,
            Message = $"Cancel failed. HTTP {(int)response.StatusCode}: {content}"
        };
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync("/v2/positions", ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Fail LOUD on error — an empty list must mean "genuinely flat".
        // Returning [] for an HTTP failure made an auth/rate-limit blip
        // indistinguishable from a flat account, and the Monitor's phantom-
        // position reconciliation would wrongly clear REAL position
        // bookkeeping on it.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Alpaca positions request failed ({(int)response.StatusCode} {response.StatusCode}): {(content.Length <= 300 ? content : content[..300] + "…")}");

        try
        {
            using var doc = JsonDocument.Parse(content);
            var positions = new List<Position>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var symbol = element.TryGetProperty("symbol", out var symProp)
                    ? symProp.GetString() ?? string.Empty
                    : string.Empty;

                // Alpaca returns qty as a string that is fractional for fractional-share
                // positions ("2.5") and negative for shorts ("-10"). Parsing as int
                // dropped both — any fractional position became 0 (reported flat) and
                // short sign was lost. Parse as decimal, invariant culture, to preserve them.
                var qty = element.TryGetProperty("qty", out var qtyProp)
                    ? decimal.TryParse(qtyProp.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedQty) ? parsedQty : 0m
                    : 0m;

                var avgPrice = element.TryGetProperty("avg_entry_price", out var avgProp)
                    ? decimal.TryParse(avgProp.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedAvg) ? parsedAvg : 0m
                    : 0m;

                var marketValue = element.TryGetProperty("market_value", out var mvProp)
                    ? decimal.TryParse(mvProp.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedMv) ? parsedMv : 0m
                    : 0m;

                var unrealizedPnl = element.TryGetProperty("unrealized_pl", out var plProp)
                    ? decimal.TryParse(plProp.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedPl) ? parsedPl : 0m
                    : 0m;

                // Options positions come back with asset_class "us_option" and the OCC
                // symbol; qty is a contract count. Decode the contract from the symbol so
                // downstream never has to parse OCC strings.
                var isOption = element.TryGetProperty("asset_class", out var acProp)
                    && string.Equals(acProp.GetString(), "us_option", StringComparison.OrdinalIgnoreCase);
                var contract = isOption ? OptionContract.ParseOcc(symbol) : null;

                positions.Add(new Position
                {
                    Symbol = symbol,
                    Quantity = qty,
                    AveragePrice = avgPrice,
                    MarketValue = marketValue,
                    UnrealizedPnl = unrealizedPnl,
                    AssetClass = contract is null ? AssetClass.Equity : AssetClass.Option,
                    Option = contract,
                });
            }

            return positions;
        }
        catch (JsonException ex)
        {
            // Same fail-loud rule: an unparseable 2xx body is NOT a flat account.
            throw new HttpRequestException(
                $"Alpaca positions response could not be parsed: {ex.Message}", ex);
        }
    }

    public async Task<Dictionary<string, string>> GetAccountAsync(CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync("/v2/account", ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new Dictionary<string, string> { ["error"] = $"HTTP {(int)response.StatusCode}" };

        try
        {
            using var doc = JsonDocument.Parse(content);
            var result = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.ToString();
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, string> { ["error"] = "Failed to parse account response." };
        }
    }

    // ---------------------------------------------------------------- Options

    /// <summary>
    /// Alpaca <c>/v2/account</c> carries <c>options_trading_level</c> (effective, 0–3),
    /// <c>options_approved_level</c> and <c>options_buying_power</c> — note the plural
    /// <c>options_</c> prefix. The first cut of this read the singular <c>option_trading_level</c>,
    /// a key Alpaca never sends, so every real account looked unapproved (level 0) and the ticket
    /// locked itself. The singular spelling is kept only as a fallback for canned fixtures.
    /// 0 = not approved; the UI locks the ticket on 0 and explains level 1 vs 2.
    /// </summary>
    public async Task<int> GetOptionTradingLevelAsync(CancellationToken ct = default) =>
        (await GetOptionsAccountAsync(ct).ConfigureAwait(false)).TradingLevel;

    public async Task<OptionsAccountInfo> GetOptionsAccountAsync(CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct).ConfigureAwait(false);
        var trading = Int(account, "options_trading_level") ?? Int(account, "option_trading_level") ?? 0;
        var approved = Int(account, "options_approved_level") ?? Int(account, "option_approved_level") ?? trading;
        var buyingPower = account.TryGetValue("options_buying_power", out var bp)
            && decimal.TryParse(bp, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var bpd)
            ? bpd : (decimal?)null;
        return new OptionsAccountInfo(trading, approved, buyingPower);

        static int? Int(Dictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var raw)
            && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v : null;
    }

    /// <summary>
    /// How far out the full chain reaches when no expiration is requested. Alpaca's
    /// <c>/v2/options/contracts</c> silently defaults <c>expiration_date_lte</c> to ONE WEEK after
    /// <c>expiration_date_gte</c> (today), so an unbounded call returned a single expiration and
    /// the chain view looked like the underlying only had weeklies. LEAPS list out to ~3 years.
    /// </summary>
    public static readonly TimeSpan ChainHorizon = TimeSpan.FromDays(3 * 366);

    /// <summary>
    /// <c>GET /v2/options/contracts</c> (trading host). Catalog only — strikes, expirations,
    /// rights, open interest. Follows <c>next_page_token</c> so a full chain comes back in one call.
    /// Without an <paramref name="expiration"/> the request spans today → <see cref="ChainHorizon"/>
    /// explicitly, because Alpaca's default window is only one week.
    /// </summary>
    public async Task<IReadOnlyList<OptionContract>> GetOptionChainAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol)) return [];
        var underlying = underlyingSymbol.Trim().ToUpperInvariant();
        var contracts = new List<OptionContract>();
        string? pageToken = null;

        for (var page = 0; page < 20; page++) // hard stop — a chain is never 20k contracts
        {
            var url = $"/v2/options/contracts?underlying_symbols={Uri.EscapeDataString(underlying)}&status=active&limit=1000";
            if (expiration is { } exp)
            {
                url += $"&expiration_date={exp:yyyy-MM-dd}";
            }
            else
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
                url += $"&expiration_date_gte={today:yyyy-MM-dd}&expiration_date_lte={today.AddDays((int)ChainHorizon.TotalDays):yyyy-MM-dd}";
            }
            if (pageToken is not null) url += $"&page_token={Uri.EscapeDataString(pageToken)}";

            using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Alpaca option contracts request failed ({(int)response.StatusCode} {response.StatusCode}): {Clip(content)}");

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("option_contracts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var parsed = ParseContract(el, underlying);
                    if (parsed is not null) contracts.Add(parsed);
                }
            }

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var tok) && tok.ValueKind == JsonValueKind.String
                ? tok.GetString()
                : null;
            if (string.IsNullOrEmpty(pageToken)) break;
        }

        return contracts;
    }

    /// <summary>
    /// <c>GET {data}/v1beta1/options/snapshots/{underlying}</c>. Per-contract latest quote/trade
    /// plus Alpaca's server-side Greeks and implied volatility, which are OMITTED (null here)
    /// for 0DTE contracts or when inputs are missing — callers fall back to the local model.
    /// </summary>
    public async Task<IReadOnlyList<OptionQuote>> GetOptionQuotesAsync(string underlyingSymbol, DateOnly? expiration = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(underlyingSymbol)) return [];
        var underlying = underlyingSymbol.Trim().ToUpperInvariant();
        var quotes = new List<OptionQuote>();
        string? pageToken = null;

        for (var page = 0; page < 20; page++)
        {
            var url = $"/v1beta1/options/snapshots/{Uri.EscapeDataString(underlying)}?feed={OptionsDataFeed}&limit=1000";
            if (expiration is { } exp) url += $"&expiration_date={exp:yyyy-MM-dd}";
            if (pageToken is not null) url += $"&page_token={Uri.EscapeDataString(pageToken)}";

            using var response = await dataClient.GetAsync(url, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Alpaca option snapshots request failed ({(int)response.StatusCode} {response.StatusCode}): {Clip(content)}");

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("snapshots", out var snaps) && snaps.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in snaps.EnumerateObject())
                    quotes.Add(ParseSnapshot(prop.Name, prop.Value));
            }

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var tok) && tok.ValueKind == JsonValueKind.String
                ? tok.GetString()
                : null;
            if (string.IsNullOrEmpty(pageToken)) break;
        }

        return quotes;
    }

    private static OptionContract? ParseContract(JsonElement el, string fallbackUnderlying)
    {
        var occ = Str(el, "symbol");
        if (string.IsNullOrEmpty(occ)) return null;

        // Prefer Alpaca's structured fields; fall back to decoding the OCC symbol.
        var decoded = OptionContract.ParseOcc(occ);
        var typeStr = Str(el, "type");
        var right = typeStr is null
            ? decoded?.Right ?? OptionRight.Call
            : typeStr.Equals("put", StringComparison.OrdinalIgnoreCase) ? OptionRight.Put : OptionRight.Call;

        return new OptionContract
        {
            OccSymbol = occ,
            UnderlyingSymbol = Str(el, "underlying_symbol") ?? decoded?.UnderlyingSymbol ?? fallbackUnderlying,
            Expiration = DateOnly.TryParse(Str(el, "expiration_date"), System.Globalization.CultureInfo.InvariantCulture, out var exp)
                ? exp : decoded?.Expiration ?? default,
            Strike = Dec(el, "strike_price") ?? decoded?.Strike ?? 0m,
            Right = right,
            Multiplier = (int)(Dec(el, "size") ?? 100m),
            Tradable = el.TryGetProperty("tradable", out var tr) && tr.ValueKind is JsonValueKind.True or JsonValueKind.False ? tr.GetBoolean() : true,
            OpenInterest = Dec(el, "open_interest") is { } oi ? (long)oi : null,
        };
    }

    private static OptionQuote ParseSnapshot(string occ, JsonElement snap)
    {
        decimal bid = 0m, ask = 0m;
        decimal? last = null;
        var ts = DateTime.UtcNow;

        if (snap.TryGetProperty("latestQuote", out var q) && q.ValueKind == JsonValueKind.Object)
        {
            bid = Num(q, "bp") ?? 0m;
            ask = Num(q, "ap") ?? 0m;
            if (q.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String && DateTime.TryParse(t.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedTs))
                ts = parsedTs;
        }
        if (snap.TryGetProperty("latestTrade", out var tr) && tr.ValueKind == JsonValueKind.Object)
            last = Num(tr, "p");

        var iv = Num(snap, "impliedVolatility");
        OptionGreeks? greeks = null;
        if (snap.TryGetProperty("greeks", out var g) && g.ValueKind == JsonValueKind.Object)
        {
            greeks = new OptionGreeks(
                Num(g, "delta") ?? 0m, Num(g, "gamma") ?? 0m, Num(g, "theta") ?? 0m, Num(g, "vega") ?? 0m, Num(g, "rho") ?? 0m);
        }

        return new OptionQuote(occ, bid, ask, last, iv, greeks, ts);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Alpaca's trading API sends numbers as strings ("38.5"); the data API sends real numbers. Accept both.</summary>
    private static decimal? Dec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDecimal(out var d) ? d : (decimal)p.GetDouble(),
            JsonValueKind.String => decimal.TryParse(p.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null,
            _ => null,
        };
    }

    private static decimal? Num(JsonElement el, string name) => Dec(el, name);

    private static string Clip(string s) => s.Length <= 300 ? s : s[..300] + "…";

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        dataClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
