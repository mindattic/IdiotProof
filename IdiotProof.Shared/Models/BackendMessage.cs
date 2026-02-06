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
        QueryStatus, // Reserved for future use
        GetStatus,
        GetOrders,
        GetIdiotProofOrders, // Get only IdiotProof-created orders
        GetPositions,
        CancelOrder,
        CancelAllOrders,
        ClosePosition,
        ReloadStrategies,
        SetStrategies, // Send strategies from frontend to backend
        ActivateStrategy,
        DeactivateStrategy,
        ActivateTrading,
        DeactivateTrading,
        ValidateStrategy, // Request strategy validation from backend
        GetTrades, // Get IdiotProof trade tracking data
        RunBacktest, // Run autonomous learning backtest for a symbol

        // Responses (Backend -> Frontend)
        StatusResponse,
        OrdersResponse,
        PositionsResponse,
        OperationResult,
        ValidationResponse, // Validation result from backend
        TradesResponse, // Trade tracking response
        BacktestResponse, // Backtest result

        // Push notifications (Backend -> Frontend)
        ConsoleOutput,
        OrderUpdate,
        PositionUpdate,
        ConnectionStatusChanged,
        StrategyStatusChanged,
        TradeUpdate, // IdiotProof trade status changed
        Ping, // Heartbeat to verify connection is alive
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
        public bool IsTradingActive { get; set; }
        public bool IsPaperTrading { get; set; }
        public int ActiveStrategies { get; set; }
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

    /// <summary>
    /// Request to validate a strategy.
    /// </summary>
    public class ValidateStrategyRequest
    {
        public StrategyDefinition? Strategy { get; set; }
    }

    /// <summary>
    /// Response to validation request.
    /// </summary>
    public class ValidationResponsePayload
    {
        public bool IsValid { get; set; }
        public List<ValidationErrorInfo> Errors { get; set; } = [];
        public List<ValidationWarningInfo> Warnings { get; set; } = [];
    }

    /// <summary>
    /// Validation error info for IPC.
    /// </summary>
    public class ValidationErrorInfo
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? FieldName { get; set; }
    }

    /// <summary>
    /// Validation warning info for IPC.
    /// </summary>
    public class ValidationWarningInfo
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? FieldName { get; set; }
    }

    /// <summary>
    /// Response to trades request.
    /// </summary>
    public class TradesResponsePayload
    {
        public List<IdiotProofTrade> Trades { get; set; } = [];
    }

    /// <summary>
    /// Request to set strategies on the backend.
    /// </summary>
    public class SetStrategiesRequest
    {
        public List<StrategyDefinition> Strategies { get; set; } = [];
    }

    /// <summary>
    /// Request to run autonomous learning backtest.
    /// </summary>
    public class RunBacktestRequest
    {
        /// <summary>
        /// Symbol to backtest.
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Number of days of historical data to use.
        /// </summary>
        public int Days { get; set; } = 30;

        /// <summary>
        /// Trading mode: Conservative, Balanced, or Aggressive.
        /// </summary>
        public string Mode { get; set; } = "Balanced";

        /// <summary>
        /// Position size for simulated trades.
        /// </summary>
        public int Quantity { get; set; } = 100;

        /// <summary>
        /// Whether to save the learned profile to disk.
        /// </summary>
        public bool SaveProfile { get; set; } = true;
    }

    /// <summary>
    /// Response from backtest run.
    /// </summary>
    public class BacktestResponsePayload
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Symbol that was backtested.
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Total number of trades simulated.
        /// </summary>
        public int TotalTrades { get; set; }

        /// <summary>
        /// Number of winning trades.
        /// </summary>
        public int WinningTrades { get; set; }

        /// <summary>
        /// Win rate as percentage.
        /// </summary>
        public double WinRate { get; set; }

        /// <summary>
        /// Total profit/loss in dollars.
        /// </summary>
        public double TotalPnL { get; set; }

        /// <summary>
        /// Average profit per trade.
        /// </summary>
        public double AvgPnL { get; set; }

        /// <summary>
        /// Number of bars processed.
        /// </summary>
        public int BarsProcessed { get; set; }

        /// <summary>
        /// Whether the profile was saved to disk.
        /// </summary>
        public bool ProfileSaved { get; set; }

        /// <summary>
        /// Profile learning confidence (0-100).
        /// </summary>
        public double Confidence { get; set; }
    }
}


