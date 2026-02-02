// ============================================================================
// IBackendService - Interface for communicating with the IdiotProof backend
// ============================================================================

using IdiotProof.Shared.Models;

namespace IdiotProof.Frontend.Services
{
    /// <summary>
    /// Service for communicating with the IdiotProof backend service.
    /// </summary>
    public interface IBackendService
    {
        /// <summary>
        /// Whether the backend is currently connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Event raised when connection status changes.
        /// </summary>
        event EventHandler<bool>? ConnectionStatusChanged;

        /// <summary>
        /// Event raised when console output is received from backend.
        /// </summary>
        event EventHandler<ConsoleOutputMessage>? ConsoleOutputReceived;

        /// <summary>
        /// Event raised when an order is updated.
        /// </summary>
        event EventHandler<OrderInfo>? OrderUpdated;

        /// <summary>
        /// Attempts to connect to the backend service.
        /// </summary>
        Task<bool> ConnectAsync();

        /// <summary>
        /// Event raised when a heartbeat is received from backend.
        /// </summary>
        event EventHandler<HeartbeatMessage>? HeartbeatReceived;

        /// <summary>
        /// Disconnects from the backend service.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Signals the backend to reload strategies from the JSON files.
        /// </summary>
        Task ReloadStrategiesAsync();

        /// <summary>
        /// Gets the backend service status.
        /// </summary>
        Task<BackendStatus> GetStatusAsync();

        /// <summary>
        /// Gets all current orders from the backend.
        /// </summary>
        Task<List<OrderInfo>> GetOrdersAsync();

        /// <summary>
        /// Gets all current positions from the backend.
        /// </summary>
        Task<List<PositionInfo>> GetPositionsAsync();

        /// <summary>
        /// Cancels an order by ID.
        /// </summary>
        Task<OperationResult> CancelOrderAsync(int orderId);

        /// <summary>
        /// Cancels all open orders.
        /// </summary>
        Task<OperationResult> CancelAllOrdersAsync();

        /// <summary>
        /// Closes a position by symbol (sells all shares).
        /// </summary>
        Task<OperationResult> ClosePositionAsync(string symbol);

        /// <summary>
        /// Gets the console output buffer.
        /// </summary>
        string GetConsoleBuffer();

        /// <summary>
        /// Activates a strategy by ID.
        /// </summary>
        Task<OperationResult> ActivateStrategyAsync(Guid strategyId);

        /// <summary>
        /// Deactivates a strategy by ID.
        /// </summary>
        Task<OperationResult> DeactivateStrategyAsync(Guid strategyId);

        /// <summary>
        /// Activates trading globally (starts all enabled strategies).
        /// </summary>
        Task<OperationResult> ActivateTradingAsync();

        /// <summary>
        /// Deactivates trading globally (stops all running strategies).
        /// </summary>
        Task<OperationResult> DeactivateTradingAsync();

        /// <summary>
        /// Gets only IdiotProof-created orders (filtered from all orders).
        /// </summary>
        Task<List<OrderInfo>> GetIdiotProofOrdersAsync();

        /// <summary>
        /// Gets all IdiotProof trades for tracking.
        /// </summary>
        Task<List<IdiotProofTrade>> GetTradesAsync();

        /// <summary>
        /// Validates a strategy on the backend.
        /// </summary>
        Task<BackendValidationResult> ValidateStrategyAsync(StrategyDefinition strategy);

        /// <summary>
        /// Event raised when a trade status changes.
        /// </summary>
        event EventHandler<IdiotProofTrade>? TradeUpdated;
    }

    /// <summary>
    /// Status information from the backend service.
    /// </summary>
    public class BackendStatus
    {
        public bool IsRunning { get; set; }
        public bool IsConnectedToIbkr { get; set; }
        public bool IsTradingActive { get; set; }
        public bool IsPaperTrading { get; set; }
        public int ActiveStrategies { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> ActiveStrategyNames { get; set; } = [];
    }

    /// <summary>
    /// Result from backend validation.
    /// </summary>
    public class BackendValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidationErrorInfo> Errors { get; set; } = [];
        public List<ValidationWarningInfo> Warnings { get; set; } = [];
    }

    /// <summary>
    /// Result of a backend operation.
    /// </summary>
    public class OperationResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }

        public static OperationResult Ok(string? message = null) => new() { Success = true, Message = message };
        public static OperationResult Fail(string error) => new() { Success = false, ErrorMessage = error };
    }
}
