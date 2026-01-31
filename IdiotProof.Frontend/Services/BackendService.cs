// ============================================================================
// BackendService - Named pipe IPC communication with IdiotProof backend
// ============================================================================

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using IdiotProof.Shared.Models;

namespace IdiotProof.Frontend.Services
{
    /// <summary>
    /// Service for communicating with the IdiotProof backend service via named pipes.
    /// </summary>
    public class BackendService : IBackendService, IDisposable
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

        private readonly StringBuilder _consoleBuffer = new(capacity: 100000);
        private readonly object _consoleLock = new();
        private readonly object _pipeLock = new();
        private readonly Dictionary<Guid, TaskCompletionSource<BackendMessage>> _pendingRequests = [];

        public bool IsConnected => _isConnected;

        public event EventHandler<bool>? ConnectionStatusChanged;
        public event EventHandler<ConsoleOutputMessage>? ConsoleOutputReceived;
        public event EventHandler<OrderInfo>? OrderUpdated;

        public async Task<bool> ConnectAsync()
        {
            if (_isConnected)
                return true;

            try
            {
                _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                using var cts = new CancellationTokenSource(ConnectionTimeoutMs);
                await _pipe.ConnectAsync(cts.Token);

                _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
                _writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                _isConnected = true;
                ConnectionStatusChanged?.Invoke(this, true);

                // Start listening for push messages
                _listenerCts = new CancellationTokenSource();
                _listenerTask = ListenForMessagesAsync(_listenerCts.Token);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] Connect failed: {ex.Message}");
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

            _reader = null;
            _writer = null;
            _pipe = null;
            _listenerCts = null;
            _listenerTask = null;

            ConnectionStatusChanged?.Invoke(this, false);
        }

        public async Task ReloadStrategiesAsync()
        {
            await SendMessageAsync(new BackendMessage { Type = BackendMessageType.ReloadStrategies });
        }

        public async Task<BackendStatus> GetStatusAsync()
        {
            if (!_isConnected)
            {
                // Try to connect first
                var connected = await ConnectAsync();
                if (!connected)
                {
                    return new BackendStatus
                    {
                        IsRunning = false,
                        IsConnectedToIbkr = false,
                        ActiveStrategies = 0,
                        ErrorMessage = "Backend not connected"
                    };
                }
            }

            try
            {
                var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetStatus });
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<StatusResponsePayload>(response.Payload);
                    if (payload != null)
                    {
                        return new BackendStatus
                        {
                            IsRunning = payload.IsRunning,
                            IsConnectedToIbkr = payload.IsConnectedToIbkr,
                            IsTradingActive = payload.IsTradingActive,
                            IsPaperTrading = payload.IsPaperTrading,
                            ActiveStrategies = payload.ActiveStrategies,
                            LastHeartbeat = payload.LastHeartbeat,
                            ErrorMessage = payload.ErrorMessage,
                            ActiveStrategyNames = payload.ActiveStrategyNames
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] GetStatus failed: {ex.Message}");
            }

            return new BackendStatus
            {
                IsRunning = _isConnected,
                ErrorMessage = "Failed to get status"
            };
        }

        public async Task<List<OrderInfo>> GetOrdersAsync()
        {
            if (!_isConnected)
                return [];

            try
            {
                var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetOrders });
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<OrdersResponsePayload>(response.Payload);
                    return payload?.Orders ?? [];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] GetOrders failed: {ex.Message}");
            }

            return [];
        }

        public async Task<List<PositionInfo>> GetPositionsAsync()
        {
            if (!_isConnected)
                return [];

            try
            {
                var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetPositions });
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<PositionsResponsePayload>(response.Payload);
                    return payload?.Positions ?? [];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] GetPositions failed: {ex.Message}");
            }

            return [];
        }

        public async Task<OperationResult> CancelOrderAsync(int orderId)
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

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
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public async Task<OperationResult> ClosePositionAsync(string symbol)
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

            try
            {
                var request = new BackendMessage
                {
                    Type = BackendMessageType.ClosePosition,
                    Payload = JsonSerializer.Serialize(new ClosePositionRequest { Symbol = symbol })
                };

                var response = await SendRequestAsync(request);
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public async Task<OperationResult> ActivateStrategyAsync(Guid strategyId)
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

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
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public async Task<OperationResult> DeactivateStrategyAsync(Guid strategyId)
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

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
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public async Task<OperationResult> CancelAllOrdersAsync()
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

            try
            {
                var request = new BackendMessage { Type = BackendMessageType.CancelAllOrders };
                var response = await SendRequestAsync(request);
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public async Task<OperationResult> ActivateTradingAsync()
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

            try
            {
                var request = new BackendMessage { Type = BackendMessageType.ActivateTrading };
                var response = await SendRequestAsync(request);
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public async Task<OperationResult> DeactivateTradingAsync()
        {
            if (!_isConnected)
                return OperationResult.Fail("Not connected to backend");

            try
            {
                var request = new BackendMessage { Type = BackendMessageType.DeactivateTrading };
                var response = await SendRequestAsync(request);
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<OperationResultPayload>(response.Payload);
                    if (payload != null)
                    {
                        return new OperationResult
                        {
                            Success = payload.Success,
                            Message = payload.Message,
                            ErrorMessage = payload.ErrorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            return OperationResult.Fail("No response from backend");
        }

        public string GetConsoleBuffer()
        {
            lock (_consoleLock)
            {
                return _consoleBuffer.ToString();
            }
        }

        public async Task<List<OrderInfo>> GetIdiotProofOrdersAsync()
        {
            if (!_isConnected)
                return [];

            try
            {
                var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetIdiotProofOrders });
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<OrdersResponsePayload>(response.Payload);
                    return payload?.Orders ?? [];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] GetIdiotProofOrders failed: {ex.Message}");
            }

            return [];
        }

        public async Task<List<IdiotProofTrade>> GetTradesAsync()
        {
            if (!_isConnected)
                return [];

            try
            {
                var response = await SendRequestAsync(new BackendMessage { Type = BackendMessageType.GetTrades });
                if (response?.Payload != null)
                {
                    var payload = JsonSerializer.Deserialize<TradesResponsePayload>(response.Payload);
                    return payload?.Trades ?? [];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] GetTrades failed: {ex.Message}");
            }

            return [];
        }

        public async Task<BackendValidationResult> ValidateStrategyAsync(StrategyDefinition strategy)
        {
            if (!_isConnected)
            {
                return new BackendValidationResult
                {
                    IsValid = false,
                    Errors = [new ValidationErrorInfo { Code = "NOT_CONNECTED", Message = "Not connected to backend" }]
                };
            }

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
                    var payload = JsonSerializer.Deserialize<ValidationResponsePayload>(response.Payload);
                    if (payload != null)
                    {
                        return new BackendValidationResult
                        {
                            IsValid = payload.IsValid,
                            Errors = payload.Errors,
                            Warnings = payload.Warnings
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BackendValidationResult
                {
                    IsValid = false,
                    Errors = [new ValidationErrorInfo { Code = "VALIDATION_ERROR", Message = ex.Message }]
                };
            }

            return new BackendValidationResult
            {
                IsValid = false,
                Errors = [new ValidationErrorInfo { Code = "NO_RESPONSE", Message = "No response from backend" }]
            };
        }

        public event EventHandler<IdiotProofTrade>? TradeUpdated;

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] SendMessage failed: {ex.Message}");
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
                    {
                        // Pipe closed
                        break;
                    }

                    try
                    {
                        var message = JsonSerializer.Deserialize<BackendMessage>(line);
                        if (message != null)
                        {
                            HandleMessage(message);
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BackendService] JSON parse error: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackendService] Listener error: {ex.Message}");
            }

            await DisconnectAsync();
        }

        private void HandleMessage(BackendMessage message)
        {
            switch (message.Type)
            {
                case BackendMessageType.ConsoleOutput:
                    if (message.Payload != null)
                    {
                        var output = JsonSerializer.Deserialize<ConsoleOutputMessage>(message.Payload);
                        if (output != null)
                        {
                            lock (_consoleLock)
                            {
                                _consoleBuffer.Append(output.Text);

                                // Trim if too large
                                if (_consoleBuffer.Length > 80000)
                                {
                                    _consoleBuffer.Remove(0, _consoleBuffer.Length - 50000);
                                    _consoleBuffer.Insert(0, "[... earlier output trimmed ...]\n");
                                }
                            }

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

                // Handle response messages
                case BackendMessageType.Pong:
                case BackendMessageType.StatusResponse:
                case BackendMessageType.OrdersResponse:
                case BackendMessageType.PositionsResponse:
                case BackendMessageType.OperationResult:
                case BackendMessageType.ValidationResponse:
                case BackendMessageType.TradesResponse:
                    if (_pendingRequests.TryGetValue(message.MessageId, out var tcs))
                    {
                        tcs.TrySetResult(message);
                    }
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DisconnectAsync().Wait(1000);
            GC.SuppressFinalize(this);
        }
    }
}
