// ============================================================================
// IpcServer - Named pipe server for frontend communication
// ============================================================================

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using IdiotProof.Shared.Models;

namespace IdiotProof.Backend.Ipc
{
    /// <summary>
    /// Named pipe server for communicating with the IdiotProof frontend.
    /// Handles requests and pushes console output and order updates.
    /// </summary>
    public sealed class IpcServer : IDisposable
    {
        private const string PipeName = "IdiotProofIPC";
        private const int MaxConnections = 5;

        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
        private Task? _acceptTask;
        private bool _disposed;

        // Callbacks for handling requests
        public Func<Task<StatusResponsePayload>>? GetStatusHandler { get; set; }
        public Func<Task<List<OrderInfo>>>? GetOrdersHandler { get; set; }
        public Func<Task<List<OrderInfo>>>? GetIdiotProofOrdersHandler { get; set; }
        public Func<Task<List<PositionInfo>>>? GetPositionsHandler { get; set; }
        public Func<int, Task<OperationResultPayload>>? CancelOrderHandler { get; set; }
        public Func<Task<OperationResultPayload>>? CancelAllOrdersHandler { get; set; }
        public Func<string, Task<OperationResultPayload>>? ClosePositionHandler { get; set; }
        public Func<Task>? ReloadStrategiesHandler { get; set; }
        public Func<List<StrategyDefinition>, Task<OperationResultPayload>>? SetStrategiesHandler { get; set; }
        public Func<Guid, Task<OperationResultPayload>>? ActivateStrategyHandler { get; set; }
        public Func<Guid, Task<OperationResultPayload>>? DeactivateStrategyHandler { get; set; }
        public Func<Task<OperationResultPayload>>? ActivateTradingHandler { get; set; }
        public Func<Task<OperationResultPayload>>? DeactivateTradingHandler { get; set; }
        public Func<StrategyDefinition, Task<ValidationResponsePayload>>? ValidateStrategyHandler { get; set; }
        public Func<Task<List<IdiotProofTrade>>>? GetTradesHandler { get; set; }

        /// <summary>
        /// Starts the IPC server.
        /// </summary>
        public void Start()
        {
            _acceptTask = AcceptClientsAsync(_cts.Token);
            Console.WriteLine("[IPC] Server started on pipe: " + PipeName);
        }

        /// <summary>
        /// Broadcasts console output to all connected clients.
        /// </summary>
        public void BroadcastConsoleOutput(string text, string level = "Info")
        {
            var message = new BackendMessage
            {
                Type = BackendMessageType.ConsoleOutput,
                Payload = JsonSerializer.Serialize(new ConsoleOutputMessage
                {
                    Text = text,
                    Timestamp = DateTime.UtcNow,
                    Level = level
                })
            };

            BroadcastMessage(message);
        }

        /// <summary>
        /// Broadcasts an order update to all connected clients.
        /// </summary>
        public void BroadcastOrderUpdate(OrderInfo order)
        {
            var message = new BackendMessage
            {
                Type = BackendMessageType.OrderUpdate,
                Payload = JsonSerializer.Serialize(order)
            };

            BroadcastMessage(message);
        }

        /// <summary>
        /// Broadcasts a trade update to all connected clients.
        /// </summary>
        public void BroadcastTradeUpdate(IdiotProofTrade trade)
        {
            var message = new BackendMessage
            {
                Type = BackendMessageType.TradeUpdate,
                Payload = JsonSerializer.Serialize(trade)
            };

            BroadcastMessage(message);
        }

        private void BroadcastMessage(BackendMessage message)
        {
            var json = JsonSerializer.Serialize(message);
            foreach (var client in _clients.Values)
            {
                try
                {
                    client.SendLine(json);
                }
                catch
                {
                    // Client disconnected
                }
            }
        }

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        MaxConnections,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(ct);

                    var clientId = Guid.NewGuid();
                    var client = new ClientConnection(clientId, pipe, HandleMessageAsync, RemoveClient);
                    _clients[clientId] = client;
                    client.Start();

                    Console.WriteLine($"[IPC] Client connected: {clientId}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[IPC] Accept error: {ex.Message}");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private void RemoveClient(Guid clientId)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                client.Dispose();
                Console.WriteLine($"[IPC] Client disconnected: {clientId}");
            }
        }

        private async Task<BackendMessage?> HandleMessageAsync(BackendMessage request)
        {
            try
            {
                switch (request.Type)
                {
                    case BackendMessageType.Ping:
                        return new BackendMessage
                        {
                            Type = BackendMessageType.Pong,
                            MessageId = request.MessageId
                        };

                    case BackendMessageType.GetStatus:
                        var status = GetStatusHandler != null
                            ? await GetStatusHandler()
                            : new StatusResponsePayload { IsRunning = true };
                        return new BackendMessage
                        {
                            Type = BackendMessageType.StatusResponse,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(status)
                        };

                    case BackendMessageType.GetOrders:
                        var orders = GetOrdersHandler != null
                            ? await GetOrdersHandler()
                            : [];
                        return new BackendMessage
                        {
                            Type = BackendMessageType.OrdersResponse,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new OrdersResponsePayload { Orders = orders })
                        };

                    case BackendMessageType.GetIdiotProofOrders:
                        var idiotProofOrders = GetIdiotProofOrdersHandler != null
                            ? await GetIdiotProofOrdersHandler()
                            : [];
                        return new BackendMessage
                        {
                            Type = BackendMessageType.OrdersResponse,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new OrdersResponsePayload { Orders = idiotProofOrders })
                        };

                    case BackendMessageType.GetPositions:
                        var positions = GetPositionsHandler != null
                            ? await GetPositionsHandler()
                            : [];
                        return new BackendMessage
                        {
                            Type = BackendMessageType.PositionsResponse,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new PositionsResponsePayload { Positions = positions })
                        };

                    case BackendMessageType.CancelOrder:
                        if (request.Payload != null && CancelOrderHandler != null)
                        {
                            var cancelReq = JsonSerializer.Deserialize<CancelOrderRequest>(request.Payload);
                            if (cancelReq != null)
                            {
                                var result = await CancelOrderHandler(cancelReq.OrderId);
                                return new BackendMessage
                                {
                                    Type = BackendMessageType.OperationResult,
                                    MessageId = request.MessageId,
                                    Payload = JsonSerializer.Serialize(result)
                                };
                            }
                        }
                        break;

                    case BackendMessageType.ClosePosition:
                        if (request.Payload != null && ClosePositionHandler != null)
                        {
                            var closeReq = JsonSerializer.Deserialize<ClosePositionRequest>(request.Payload);
                            if (closeReq != null)
                            {
                                var result = await ClosePositionHandler(closeReq.Symbol);
                                return new BackendMessage
                                {
                                    Type = BackendMessageType.OperationResult,
                                    MessageId = request.MessageId,
                                    Payload = JsonSerializer.Serialize(result)
                                };
                            }
                        }
                        break;

                    case BackendMessageType.ReloadStrategies:
                        if (ReloadStrategiesHandler != null)
                        {
                            await ReloadStrategiesHandler();
                        }
                        return new BackendMessage
                        {
                            Type = BackendMessageType.OperationResult,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new OperationResultPayload { Success = true, Message = "Strategies reloaded" })
                        };

                    case BackendMessageType.SetStrategies:
                        if (request.Payload != null && SetStrategiesHandler != null)
                        {
                            var setReq = JsonSerializer.Deserialize<SetStrategiesRequest>(request.Payload);
                            if (setReq != null)
                            {
                                var result = await SetStrategiesHandler(setReq.Strategies);
                                return new BackendMessage
                                {
                                    Type = BackendMessageType.OperationResult,
                                    MessageId = request.MessageId,
                                    Payload = JsonSerializer.Serialize(result)
                                };
                            }
                        }
                        return new BackendMessage
                        {
                            Type = BackendMessageType.OperationResult,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new OperationResultPayload { Success = false, ErrorMessage = "No strategies provided" })
                        };

                    case BackendMessageType.ActivateStrategy:
                        if (request.Payload != null && ActivateStrategyHandler != null)
                        {
                            var activateReq = JsonSerializer.Deserialize<StrategyActionRequest>(request.Payload);
                            if (activateReq != null)
                            {
                                var result = await ActivateStrategyHandler(activateReq.StrategyId);
                                return new BackendMessage
                                {
                                    Type = BackendMessageType.OperationResult,
                                    MessageId = request.MessageId,
                                    Payload = JsonSerializer.Serialize(result)
                                };
                            }
                        }
                        break;

                    case BackendMessageType.DeactivateStrategy:
                        if (request.Payload != null && DeactivateStrategyHandler != null)
                        {
                            var deactivateReq = JsonSerializer.Deserialize<StrategyActionRequest>(request.Payload);
                            if (deactivateReq != null)
                            {
                                var result = await DeactivateStrategyHandler(deactivateReq.StrategyId);
                                return new BackendMessage
                                {
                                    Type = BackendMessageType.OperationResult,
                                    MessageId = request.MessageId,
                                    Payload = JsonSerializer.Serialize(result)
                                };
                            }
                        }
                        break;

                    case BackendMessageType.CancelAllOrders:
                        if (CancelAllOrdersHandler != null)
                        {
                            var result = await CancelAllOrdersHandler();
                            return new BackendMessage
                            {
                                Type = BackendMessageType.OperationResult,
                                MessageId = request.MessageId,
                                Payload = JsonSerializer.Serialize(result)
                            };
                        }
                        break;

                    case BackendMessageType.ActivateTrading:
                        if (ActivateTradingHandler != null)
                        {
                            var result = await ActivateTradingHandler();
                            return new BackendMessage
                            {
                                Type = BackendMessageType.OperationResult,
                                MessageId = request.MessageId,
                                Payload = JsonSerializer.Serialize(result)
                            };
                        }
                        break;

                    case BackendMessageType.DeactivateTrading:
                        if (DeactivateTradingHandler != null)
                        {
                            var result = await DeactivateTradingHandler();
                            return new BackendMessage
                            {
                                Type = BackendMessageType.OperationResult,
                                MessageId = request.MessageId,
                                Payload = JsonSerializer.Serialize(result)
                            };
                        }
                        break;

                    case BackendMessageType.ValidateStrategy:
                        if (request.Payload != null && ValidateStrategyHandler != null)
                        {
                            var validateReq = JsonSerializer.Deserialize<ValidateStrategyRequest>(request.Payload);
                            if (validateReq?.Strategy != null)
                            {
                                var validationResult = await ValidateStrategyHandler(validateReq.Strategy);
                                return new BackendMessage
                                {
                                    Type = BackendMessageType.ValidationResponse,
                                    MessageId = request.MessageId,
                                    Payload = JsonSerializer.Serialize(validationResult)
                                };
                            }
                        }
                        return new BackendMessage
                        {
                            Type = BackendMessageType.ValidationResponse,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new ValidationResponsePayload 
                            { 
                                IsValid = false, 
                                Errors = [new ValidationErrorInfo { Code = "NO_STRATEGY", Message = "No strategy provided" }]
                            })
                        };

                    case BackendMessageType.GetTrades:
                        var trades = GetTradesHandler != null
                            ? await GetTradesHandler()
                            : [];
                        return new BackendMessage
                        {
                            Type = BackendMessageType.TradesResponse,
                            MessageId = request.MessageId,
                            Payload = JsonSerializer.Serialize(new TradesResponsePayload { Trades = trades })
                        };
                }
            }
            catch (Exception ex)
            {
                return new BackendMessage
                {
                    Type = BackendMessageType.OperationResult,
                    MessageId = request.MessageId,
                    Payload = JsonSerializer.Serialize(new OperationResultPayload
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    })
                };
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();

            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();

            _acceptTask?.Wait(1000);
            _cts.Dispose();

            // Clear handler references to prevent memory leaks
            GetStatusHandler = null;
            GetOrdersHandler = null;
            GetIdiotProofOrdersHandler = null;
            GetPositionsHandler = null;
            CancelOrderHandler = null;
            CancelAllOrdersHandler = null;
            ClosePositionHandler = null;
            ReloadStrategiesHandler = null;
            ActivateStrategyHandler = null;
            DeactivateStrategyHandler = null;
            ActivateTradingHandler = null;
            DeactivateTradingHandler = null;
            ValidateStrategyHandler = null;
            GetTradesHandler = null;
        }

        /// <summary>
        /// Represents a connected client.
        /// </summary>
        private sealed class ClientConnection : IDisposable
        {
            private readonly Guid _id;
            private readonly NamedPipeServerStream _pipe;
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;
            private readonly Func<BackendMessage, Task<BackendMessage?>> _messageHandler;
            private readonly Action<Guid> _onDisconnect;
            private readonly CancellationTokenSource _cts = new();
            private readonly object _writeLock = new();
            private Task? _readTask;

            public ClientConnection(
                Guid id,
                NamedPipeServerStream pipe,
                Func<BackendMessage, Task<BackendMessage?>> messageHandler,
                Action<Guid> onDisconnect)
            {
                _id = id;
                _pipe = pipe;
                _reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                _writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                _messageHandler = messageHandler;
                _onDisconnect = onDisconnect;
            }

            public void Start()
            {
                _readTask = ReadMessagesAsync(_cts.Token);
            }

            public void SendLine(string json)
            {
                lock (_writeLock)
                {
                    try
                    {
                        _writer.WriteLine(json);
                    }
                    catch
                    {
                        // Pipe broken
                    }
                }
            }

            private async Task ReadMessagesAsync(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested && _pipe.IsConnected)
                    {
                        var line = await _reader.ReadLineAsync(ct);
                        if (line == null)
                            break;

                        try
                        {
                            var message = JsonSerializer.Deserialize<BackendMessage>(line);
                            if (message != null)
                            {
                                var response = await _messageHandler(message);
                                if (response != null)
                                {
                                    SendLine(JsonSerializer.Serialize(response));
                                }
                            }
                        }
                        catch (JsonException)
                        {
                            // Invalid JSON, ignore
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
                catch
                {
                    // Connection error
                }
                finally
                {
                    _onDisconnect(_id);
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _reader.Dispose();
                _writer.Dispose();
                _pipe.Dispose();
                _cts.Dispose();
            }
        }
    }

    /// <summary>
    /// TextWriter that broadcasts to IPC clients.
    /// Wraps the original console output and sends to frontend.
    /// </summary>
    public sealed class IpcBroadcastingTextWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly IpcServer _server;
        private readonly StringBuilder _lineBuffer = new();
        private readonly object _lock = new();

        public IpcBroadcastingTextWriter(TextWriter original, IpcServer server)
        {
            _original = original;
            _server = server;
        }

        public override Encoding Encoding => _original.Encoding;

        public override void Write(char value)
        {
            _original.Write(value);
            
            lock (_lock)
            {
                if (value == '\n')
                {
                    FlushLine();
                }
                else
                {
                    _lineBuffer.Append(value);
                }
            }
        }

        public override void Write(string? value)
        {
            _original.Write(value);
            
            if (string.IsNullOrEmpty(value))
                return;

            lock (_lock)
            {
                foreach (var c in value)
                {
                    if (c == '\n')
                    {
                        FlushLine();
                    }
                    else
                    {
                        _lineBuffer.Append(c);
                    }
                }
            }
        }

        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            
            lock (_lock)
            {
                _lineBuffer.Append(value);
                FlushLine();
            }
        }

        public override void WriteLine()
        {
            _original.WriteLine();
            
            lock (_lock)
            {
                FlushLine();
            }
        }

        private void FlushLine()
        {
            var line = _lineBuffer.ToString();
            _lineBuffer.Clear();
            
            if (!string.IsNullOrWhiteSpace(line))
            {
                var level = DetermineLevel(line);
                _server.BroadcastConsoleOutput(line + "\n", level);
            }
        }

        private static string DetermineLevel(string line)
        {
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase))
                return "Error";
            if (line.Contains("WARNING", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("WARN", StringComparison.OrdinalIgnoreCase))
                return "Warning";
            return "Info";
        }
    }
}
