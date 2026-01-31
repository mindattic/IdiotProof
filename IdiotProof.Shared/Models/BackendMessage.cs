// ============================================================================
// BackendMessage - IPC message types for backend/frontend communication
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Shared.Models
{
    /// <summary>
    /// Types of messages exchanged between backend and frontend.
    /// </summary>
    public enum BackendMessageType
    {
        // Requests (Frontend -> Backend)
        Ping,
        GetStatus,
        GetOrders,
        GetPositions,
        CancelOrder,
        ClosePosition,
        ReloadStrategies,
        ActivateStrategy,
        DeactivateStrategy,

        // Responses (Backend -> Frontend)
        Pong,
        StatusResponse,
        OrdersResponse,
        PositionsResponse,
        OperationResult,

        // Push notifications (Backend -> Frontend)
        ConsoleOutput,
        OrderUpdate,
        PositionUpdate,
        ConnectionStatusChanged,
        StrategyStatusChanged
    }

    /// <summary>
    /// Base message for IPC communication.
    /// </summary>
    public class BackendMessage
    {
        /// <summary>
        /// Message type.
        /// </summary>
        public BackendMessageType Type { get; set; }

        /// <summary>
        /// Unique message ID for request/response correlation.
        /// </summary>
        public Guid MessageId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Timestamp when message was created.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// JSON payload data.
        /// </summary>
        public string? Payload { get; set; }
    }

    /// <summary>
    /// Console output message pushed to frontend.
    /// </summary>
    public class ConsoleOutputMessage
    {
        /// <summary>
        /// The console text.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp of the output.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Output level (Info, Warning, Error).
        /// </summary>
        public string Level { get; set; } = "Info";
    }

    /// <summary>
    /// Response to status request.
    /// </summary>
    public class StatusResponsePayload
    {
        public bool IsRunning { get; set; }
        public bool IsConnectedToIbkr { get; set; }
        public int ActiveStrategies { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> ActiveStrategyNames { get; set; } = [];
    }

    /// <summary>
    /// Response to orders request.
    /// </summary>
    public class OrdersResponsePayload
    {
        public List<OrderInfo> Orders { get; set; } = [];
    }

    /// <summary>
    /// Response to positions request.
    /// </summary>
    public class PositionsResponsePayload
    {
        public List<PositionInfo> Positions { get; set; } = [];
    }

    /// <summary>
    /// Request to cancel an order.
    /// </summary>
    public class CancelOrderRequest
    {
        public int OrderId { get; set; }
    }

    /// <summary>
    /// Request to close a position.
    /// </summary>
    public class ClosePositionRequest
    {
        public string Symbol { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request to activate/deactivate a strategy.
    /// </summary>
    public class StrategyActionRequest
    {
        public Guid StrategyId { get; set; }
    }

    /// <summary>
    /// Result of an operation.
    /// </summary>
    public class OperationResultPayload
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
