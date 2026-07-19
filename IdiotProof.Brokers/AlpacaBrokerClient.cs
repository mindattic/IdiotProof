using System.Net.Http.Json;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Alpaca broker implementation using REST API.
/// </summary>
public sealed class AlpacaBrokerClient : IBrokerClient, IAsyncDisposable
{
    private readonly HttpClient httpClient;
    private bool connected;

    public BrokerType BrokerType => BrokerType.Alpaca;
    public bool IsConnected => connected;

    public AlpacaBrokerClient(string apiKeyId, string apiSecretKey, bool isPaper = true)
    {
        var baseUri = isPaper
            ? "https://paper-api.alpaca.markets"
            : "https://api.alpaca.markets";

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUri),
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(apiKeyId) && !string.IsNullOrWhiteSpace(apiSecretKey))
        {
            httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKeyId);
            httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey);
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
                symbol         = request.Symbol.ToUpperInvariant(),
                qty            = request.Quantity,
                side,
                type,
                time_in_force  = request.TimeInForce.ToLowerInvariant(),
                limit_price    = limitPrice,
                stop_price     = stopPrice,
                trail_percent  = trailPct,
                extended_hours = request.ExtendedHours,
            }
            : new
            {
                symbol         = request.Symbol.ToUpperInvariant(),
                notional       = request.Notional!.Value,
                side,
                type,
                time_in_force  = request.TimeInForce.ToLowerInvariant(),
                limit_price    = limitPrice,
                stop_price     = stopPrice,
                trail_percent  = trailPct,
                extended_hours = request.ExtendedHours,
            };

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

                positions.Add(new Position
                {
                    Symbol = symbol,
                    Quantity = qty,
                    AveragePrice = avgPrice,
                    MarketValue = marketValue,
                    UnrealizedPnl = unrealizedPnl
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

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
