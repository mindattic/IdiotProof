// ============================================================================
// TradeExecutionService - Bridges Web UI to Core trading engine
// ============================================================================

using IdiotProof.Shared;
using IdiotProof.Web.Services.MarketScanner;

namespace IdiotProof.Web.Services;

/// <summary>
/// Service for executing trades from the Web UI through the Core backend.
/// </summary>
public sealed class TradeExecutionService
{
    private readonly ILogger<TradeExecutionService> _logger;
    private readonly HttpClient _httpClient;
    
    // Connection to IdiotProof.Core (when running)
    private string _coreApiUrl = "http://localhost:5050";
    private bool _isConnected;
    
    public bool IsConnected => _isConnected;
    
    public TradeExecutionService(HttpClient httpClient, ILogger<TradeExecutionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }
    
    /// <summary>
    /// Checks if IdiotProof.Core is running and connected to IBKR.
    /// </summary>
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_coreApiUrl}/status");
            _isConnected = response.IsSuccessStatusCode;
            return _isConnected;
        }
        catch
        {
            _isConnected = false;
            return false;
        }
    }

    /// <summary>
    /// Executes a trade setup directly (from Trade page).
    /// </summary>
    public async Task<TradeExecutionResult> ExecuteTradeAsync(TradeSetup setup)
    {
        _logger.LogInformation("Executing {Direction} trade for {Symbol}: Entry ${Entry}, SL ${SL}, TP ${TP}, Qty {Qty}",
            setup.Direction, setup.Symbol, setup.EntryPrice, setup.StopLoss, setup.TakeProfit, setup.Quantity);

        // Convert to scenario and execute
        var scenario = new TradeScenario
        {
            ScenarioId = Guid.NewGuid().ToString("N")[..8],
            Symbol = setup.Symbol,
            IsLong = setup.IsLong,
            EntryPrice = setup.EntryPrice,
            StopLoss = setup.StopLoss,
            TakeProfit = setup.TakeProfit,
            TrailingStopPercent = 1.5,
            Quantity = setup.Quantity
        };

        return await ExecuteScenarioAsync(scenario);
    }

    /// <summary>
    /// Executes a trade scenario through the Core backend.
    /// </summary>
    public async Task<TradeExecutionResult> ExecuteScenarioAsync(TradeScenario scenario)
    {
        _logger.LogInformation("Executing {Direction} trade for {Symbol}: Entry ${Entry}, SL ${SL}, TP ${TP}, Qty {Qty}",
            scenario.Direction, scenario.Symbol, scenario.EntryPrice, scenario.StopLoss, scenario.TakeProfit, scenario.Quantity);
        
        // TODO: When IdiotProof.Core exposes an API, call it here
        // For now, we'll create a pending order that the user can execute from Core
        
        // Store the scenario for later execution
        var pendingOrder = new PendingTradeOrder
        {
            ScenarioId = scenario.ScenarioId,
            Symbol = scenario.Symbol,
            IsLong = scenario.IsLong,
            EntryPrice = scenario.EntryPrice,
            StopLoss = scenario.StopLoss,
            TakeProfit = scenario.TakeProfit,
            TrailingStopPercent = scenario.TrailingStopPercent,
            Quantity = scenario.Quantity,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(10)
        };
        
        // Save to file for Core to pick up
        await SavePendingOrderAsync(pendingOrder);
        
        return new TradeExecutionResult
        {
            Success = true,
            Message = $"Trade setup saved. Execute from IdiotProof console or wait for Core API integration.",
            ScenarioId = scenario.ScenarioId,
            OrderId = pendingOrder.Id
        };
    }
    
    private async Task SavePendingOrderAsync(PendingTradeOrder order)
    {
        try
        {
            var ordersDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IdiotProof", "PendingOrders");
            
            Directory.CreateDirectory(ordersDir);
            
            var filePath = Path.Combine(ordersDir, $"{order.Id}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(order, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogInformation("Saved pending order: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pending order");
        }
    }
    
    /// <summary>
    /// Gets all pending orders.
    /// </summary>
    public async Task<List<PendingTradeOrder>> GetPendingOrdersAsync()
    {
        var orders = new List<PendingTradeOrder>();
        
        try
        {
            var ordersDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IdiotProof", "PendingOrders");
            
            if (!Directory.Exists(ordersDir))
                return orders;
            
            foreach (var file in Directory.GetFiles(ordersDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var order = System.Text.Json.JsonSerializer.Deserialize<PendingTradeOrder>(json);
                    
                    if (order != null)
                    {
                        if (order.ExpiresUtc > DateTime.UtcNow)
                        {
                            orders.Add(order);
                        }
                        else
                        {
                            // Clean up expired
                            File.Delete(file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load pending order: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending orders");
        }
        
        return orders.OrderByDescending(o => o.CreatedUtc).ToList();
    }
}

/// <summary>
/// Result of a trade execution attempt.
/// </summary>
public sealed class TradeExecutionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScenarioId { get; set; }
    public string? OrderId { get; set; }
}

/// <summary>
/// A pending trade order waiting to be executed.
/// </summary>
public sealed class PendingTradeOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string ScenarioId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public bool IsLong { get; set; }
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double TrailingStopPercent { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string Status { get; set; } = "Pending";
}
