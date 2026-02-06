// ============================================================================
// BackendClient - Named pipe IPC client for communicating with IdiotProof backend
// ============================================================================

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using IdiotProof.Shared.Models;

namespace IdiotProof.Console.Services;

/// <summary>
/// Client for communicating with the IdiotProof backend service via named pipes.
/// </summary>
public sealed class BackendClient : IDisposable
{
    private const string PipeName = "IdiotProofIPC";
    private const int ConnectionTimeoutMs = 5000;
    private const int ReadTimeoutMs = 10000;

    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _isConnected;
    private bool _disposed;

    private readonly object _pipeLock = new();
    private readonly Dictionary<Guid, TaskCompletionSource<BackendMessage>> _pendingRequests = [];

    public bool IsConnected => _isConnected;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<ConsoleOutputMessage>? ConsoleOutputReceived;
    public event EventHandler<OrderInfo>? OrderUpdated;
    public event EventHandler<IdiotProofTrade>? TradeUpdated;
    public event EventHandler? PingReceived;

    public async Task<bool> ConnectAsync()
    {
        if (_isConnected)
            return true;

        try
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(ConnectionTimeoutMs);
            await _pipe.ConnectAsync(cts.Token);

            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            _reader = new StreamReader(_pipe, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(_pipe, utf8NoBom, bufferSize: 4096, leaveOpen: true);
            _writer.AutoFlush = true;

            _isConnected = true;

            _listenerCts = new CancellationTokenSource();
            _listenerTask = ListenForMessagesAsync(_listenerCts.Token);

            ConnectionStatusChanged?.Invoke(this, true);
            return true;
        }
        catch (OperationCanceledException)
        {
            await DisconnectAsync();
            return false;
        }
        catch
        {
            await DisconnectAsync();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        _isConnected = false;

        _listenerCts?.Cancel();
        if (_listenerTask != null)
        {
            try { await _listenerTask; } catch { }
        }

        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
        _listenerCts?.Dispose();

        _reader = null;
        _writer = null;
        _pipe = null;
        _listenerCts = null;
        _listenerTask = null;

        foreach (var tcs in _pendingRequests.Values)
        {
            tcs.TrySetCanceled();
        }
        _pendingRequests.Clear();

        ConnectionStatusChanged?.Invoke(this, false);
    }

    public async Task<StatusResponsePayload?> GetStatusAsync()
    {
        if (!_isConnected) return null;

        try
        {
            var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetStatus });
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<StatusResponsePayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<List<OrderInfo>> GetOrdersAsync()
    {
        if (!_isConnected) return [];

        try
        {
            var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetOrders });
            if (response?.Payload != null)
            {
                var payload = JsonSerializer.Deserialize<OrdersResponsePayload>(response.Payload);
                return payload?.Orders ?? [];
            }
        }
        catch { }

        return [];
    }

    public async Task<List<PositionInfo>> GetPositionsAsync()
    {
        if (!_isConnected) return [];

        try
        {
            var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetPositions });
            if (response?.Payload != null)
            {
                var payload = JsonSerializer.Deserialize<PositionsResponsePayload>(response.Payload);
                return payload?.Positions ?? [];
            }
        }
        catch { }

        return [];
    }

    public async Task<OperationResultPayload?> CancelOrderAsync(int orderId)
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage
            {
                Type = BackendMessageType.CancelOrder,
                Payload = JsonSerializer.Serialize(new CancelOrderRequest { OrderId = orderId })
            };

            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<OperationResultPayload?> CancelAllOrdersAsync()
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage { Type = BackendMessageType.CancelAllOrders };
            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<OperationResultPayload?> ActivateTradingAsync()
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage { Type = BackendMessageType.ActivateTrading };
            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<OperationResultPayload?> DeactivateTradingAsync()
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage { Type = BackendMessageType.DeactivateTrading };
            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<OperationResultPayload?> ActivateStrategyAsync(Guid strategyId)
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage
            {
                Type = BackendMessageType.ActivateStrategy,
                Payload = JsonSerializer.Serialize(new StrategyActionRequest { StrategyId = strategyId })
            };

            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<OperationResultPayload?> DeactivateStrategyAsync(Guid strategyId)
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage
            {
                Type = BackendMessageType.DeactivateStrategy,
                Payload = JsonSerializer.Serialize(new StrategyActionRequest { StrategyId = strategyId })
            };

            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<ValidationResponsePayload?> ValidateStrategyAsync(StrategyDefinition strategy)
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage
            {
                Type = BackendMessageType.ValidateStrategy,
                Payload = JsonSerializer.Serialize(new ValidateStrategyRequest { Strategy = strategy })
            };

            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<ValidationResponsePayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task ReloadStrategiesAsync()
    {
        if (!_isConnected) return;
        await SendMessageAsync(new BackendMessage { Type = BackendMessageType.ReloadStrategies });
    }

    public async Task<OperationResultPayload?> SetStrategiesAsync(List<StrategyDefinition> strategies)
    {
        if (!_isConnected) return null;

        try
        {
            var request = new BackendMessage
            {
                Type = BackendMessageType.SetStrategies,
                Payload = JsonSerializer.Serialize(new SetStrategiesRequest { Strategies = strategies })
            };

            var response = await SendRequestAsync(request);
            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    public async Task<List<IdiotProofTrade>> GetTradesAsync()
    {
        if (!_isConnected) return [];

        try
        {
            var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetTrades });
            if (response?.Payload != null)
            {
                var payload = JsonSerializer.Deserialize<TradesResponsePayload>(response.Payload);
                return payload?.Trades ?? [];
            }
        }
        catch { }

        return [];
    }

    /// <summary>
    /// Runs an autonomous learning backtest for a symbol.
    /// </summary>
    /// <param name="symbol">Symbol to backtest.</param>
    /// <param name="days">Number of days of historical data (default 30).</param>
    /// <param name="mode">Trading mode: Conservative, Balanced, or Aggressive.</param>
    /// <param name="quantity">Position size for simulated trades.</param>
    /// <param name="saveProfile">Whether to save the learned profile.</param>
    /// <returns>Backtest response with results.</returns>
    public async Task<BacktestResponsePayload?> RunBacktestAsync(
        string symbol,
        int days = 30,
        string mode = "Balanced",
        int quantity = 100,
        bool saveProfile = true)
    {
        if (!_isConnected) return null;

        try
        {
            var request = new RunBacktestRequest
            {
                Symbol = symbol.ToUpperInvariant(),
                Days = days,
                Mode = mode,
                Quantity = quantity,
                SaveProfile = saveProfile
            };

            // Use longer timeout for backtest (may take a while to fetch data)
            var response = await SendRequestAsync(
                new BackendMessage
                {
                    Type = BackendMessageType.RunBacktest,
                    Payload = JsonSerializer.Serialize(request)
                },
                timeoutMs: 120000); // 2 minute timeout

            if (response?.Payload != null)
            {
                return JsonSerializer.Deserialize<BacktestResponsePayload>(response.Payload);
            }
        }
        catch { }

        return null;
    }

    private async Task SendMessageAsync(BackendMessage message)
    {
        if (_writer == null || !_isConnected)
            return;

        try
        {
            lock (_pipeLock)
            {
                var json = JsonSerializer.Serialize(message);
                _writer.WriteLine(json);
            }
        }
        catch
        {
            await DisconnectAsync();
        }
    }

    private async Task<BackendMessage?> SendRequestAsync(BackendMessage request, int timeoutMs = ReadTimeoutMs)
    {
        if (_writer == null || !_isConnected)
            return null;

        var tcs = new TaskCompletionSource<BackendMessage>();
        _pendingRequests[request.MessageId] = tcs;

        try
        {
            await SendMessageAsync(request);

            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingRequests.Remove(request.MessageId);
        }
    }

    private async Task ListenForMessagesAsync(CancellationToken ct)
    {
        if (_reader == null)
            return;

        try
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                    break;

                try
                {
                    var message = JsonSerializer.Deserialize<BackendMessage>(line);
                    if (message != null)
                    {
                        HandleMessage(message);
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch { }

        await DisconnectAsync();
    }

    private void HandleMessage(BackendMessage message)
    {
        // Check if this is a response to a pending request
        if (_pendingRequests.TryGetValue(message.MessageId, out var tcs))
        {
            tcs.TrySetResult(message);
            return;
        }

        // Handle push notifications
        switch (message.Type)
        {
            case BackendMessageType.ConsoleOutput:
                if (message.Payload != null)
                {
                    var output = JsonSerializer.Deserialize<ConsoleOutputMessage>(message.Payload);
                    if (output != null)
                    {
                        ConsoleOutputReceived?.Invoke(this, output);
                    }
                }
                break;

            case BackendMessageType.OrderUpdate:
                if (message.Payload != null)
                {
                    var order = JsonSerializer.Deserialize<OrderInfo>(message.Payload);
                    if (order != null)
                    {
                        OrderUpdated?.Invoke(this, order);
                    }
                }
                break;

            case BackendMessageType.TradeUpdate:
                if (message.Payload != null)
                {
                    var trade = JsonSerializer.Deserialize<IdiotProofTrade>(message.Payload);
                    if (trade != null)
                    {
                        TradeUpdated?.Invoke(this, trade);
                    }
                }
                break;

            case BackendMessageType.Ping:
                PingReceived?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
    }
}


