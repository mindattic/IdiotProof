using System.Net.Http.Json;
using System.Text.Json;
using IdiotProof.Models;

namespace IdiotProof.Brokers;

/// <summary>
/// Alpaca broker implementation using REST API.
/// </summary>
public sealed class AlpacaBrokerClient : IBrokerClient, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private bool _connected;

    public BrokerType BrokerType => BrokerType.Alpaca;
    public bool IsConnected => _connected;

    public AlpacaBrokerClient(string apiKeyId, string apiSecretKey, bool isPaper = true)
    {
        var baseUri = isPaper
            ? "https://paper-api.alpaca.markets"
            : "https://api.alpaca.markets";

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUri) };

        if (!string.IsNullOrWhiteSpace(apiKeyId) && !string.IsNullOrWhiteSpace(apiSecretKey))
        {
            _httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKeyId);
            _httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey);
        }
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0)
        {
            return new OrderResult { IsSuccess = false, Message = "Invalid symbol or quantity." };
        }

        var side = request.Side == OrderSide.Buy ? "buy" : "sell";
        var type = request.Type == OrderType.Market ? "market" : "limit";

        var payload = new
        {
            symbol = request.Symbol.ToUpperInvariant(),
            qty = request.Quantity,
            side,
            type,
            time_in_force = request.TimeInForce.ToLowerInvariant(),
            limit_price = request.Type == OrderType.Limit ? request.LimitPrice : null
        };

        using var response = await _httpClient.PostAsJsonAsync("/v2/orders", payload, ct).ConfigureAwait(false);
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

    public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        // TODO: implement DELETE /v2/orders/{orderId}
        return Task.FromResult(new OrderResult { BrokerOrderId = orderId, IsSuccess = false, Message = "Cancel not yet implemented." });
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        // TODO: implement GET /v2/positions
        IReadOnlyList<Position> empty = [];
        return Task.FromResult(empty);
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
