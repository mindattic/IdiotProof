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
        var type = request.Type == OrderType.Market ? "market" : "limit";

        // Alpaca's /v2/orders accepts qty XOR notional. We send only the field
        // that's actually set so the API doesn't 422 on the "both" case.
        object payload = hasShares
            ? new
            {
                symbol        = request.Symbol.ToUpperInvariant(),
                qty           = request.Quantity,
                side,
                type,
                time_in_force = request.TimeInForce.ToLowerInvariant(),
                limit_price   = request.Type == OrderType.Limit ? request.LimitPrice : null,
            }
            : new
            {
                symbol        = request.Symbol.ToUpperInvariant(),
                notional      = request.Notional!.Value,
                side,
                type,
                time_in_force = request.TimeInForce.ToLowerInvariant(),
                limit_price   = request.Type == OrderType.Limit ? request.LimitPrice : null,
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
        catch
        {
            return new OrderResult { IsSuccess = false, Message = "Order may have placed but response parse failed." };
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

        if (!response.IsSuccessStatusCode)
            return [];

        try
        {
            using var doc = JsonDocument.Parse(content);
            var positions = new List<Position>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var symbol = element.TryGetProperty("symbol", out var symProp)
                    ? symProp.GetString() ?? string.Empty
                    : string.Empty;

                var qty = element.TryGetProperty("qty", out var qtyProp)
                    ? int.TryParse(qtyProp.GetString(), out var parsedQty) ? parsedQty : 0
                    : 0;

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
        catch
        {
            return [];
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
