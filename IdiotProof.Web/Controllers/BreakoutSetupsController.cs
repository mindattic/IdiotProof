// ============================================================================
// Breakout Setup API Controller - Endpoints for breakout-pullback strategies
// ============================================================================
// Provides API access to:
// - Get active breakout setups
// - Generate IdiotScript strategies
// - Trigger rescans
// - Get strategy cards in pro trader format
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using IdiotProof.Scripting;
using IdiotProof.Web.Services.MarketScanner;

namespace IdiotProof.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BreakoutSetupsController : ControllerBase
{
    private readonly BreakoutSetupService _setupService;
    private readonly ILogger<BreakoutSetupsController> _logger;
    
    public BreakoutSetupsController(
        BreakoutSetupService setupService,
        ILogger<BreakoutSetupsController> logger)
    {
        _setupService = setupService;
        _logger = logger;
    }
    
    /// <summary>
    /// Gets all active breakout-pullback setups.
    /// GET /api/breakoutsetups
    /// </summary>
    [HttpGet]
    public IActionResult GetSetups()
    {
        var setups = _setupService.GetActiveSetups();
        return Ok(setups.Select(s => new
        {
            s.Symbol,
            s.CompanyName,
            s.Bias,
            s.Pattern,
            s.ConfidenceScore,
            s.TriggerPrice,
            s.SupportPrice,
            s.VwapPrice,
            s.InvalidationPrice,
            Targets = s.Targets.Select(t => new { t.Label, t.Price, t.PercentToSell, t.IsHit }),
            s.CurrentPrice,
            s.GapPercent,
            s.VolumeRatio,
            s.RiskPercent,
            s.RewardPercent,
            s.RiskRewardRatio,
            State = s.State.ToString(),
            s.DiscoveredUtc,
            s.TriggeredUtc,
            s.ConfirmedUtc
        }));
    }
    
    /// <summary>
    /// Gets a specific setup by symbol.
    /// GET /api/breakoutsetups/{symbol}
    /// </summary>
    [HttpGet("{symbol}")]
    public IActionResult GetSetup(string symbol)
    {
        var setup = _setupService.GetSetup(symbol);
        if (setup == null)
            return NotFound($"No setup found for {symbol}");
        
        return Ok(new
        {
            setup.Symbol,
            setup.CompanyName,
            setup.Bias,
            setup.Pattern,
            setup.ConfidenceScore,
            setup.TriggerPrice,
            setup.SupportPrice,
            setup.VwapPrice,
            setup.InvalidationPrice,
            Targets = setup.Targets.Select(t => new { t.Label, t.Price, t.PercentToSell, t.IsHit }),
            setup.CurrentPrice,
            setup.GapPercent,
            setup.VolumeRatio,
            setup.RiskPercent,
            setup.RewardPercent,
            setup.RiskRewardRatio,
            State = setup.State.ToString(),
            setup.DiscoveredUtc,
            setup.TriggeredUtc,
            setup.ConfirmedUtc,
            IdiotScript = setup.ToIdiotScript(),
            StrategyCard = setup.ToStrategyCard()
        });
    }
    
    /// <summary>
    /// Gets the IdiotScript for a specific setup.
    /// GET /api/breakoutsetups/{symbol}/script
    /// </summary>
    [HttpGet("{symbol}/script")]
    public IActionResult GetScript(string symbol)
    {
        var setup = _setupService.GetSetup(symbol);
        if (setup == null)
            return NotFound($"No setup found for {symbol}");
        
        return Ok(new
        {
            setup.Symbol,
            Script = setup.ToIdiotScript()
        });
    }
    
    /// <summary>
    /// Gets the strategy card in pro trader format for a specific setup.
    /// GET /api/breakoutsetups/{symbol}/card
    /// </summary>
    [HttpGet("{symbol}/card")]
    public IActionResult GetCard(string symbol)
    {
        var setup = _setupService.GetSetup(symbol);
        if (setup == null)
            return NotFound($"No setup found for {symbol}");
        
        return Ok(new
        {
            setup.Symbol,
            Card = setup.ToStrategyCard()
        });
    }
    
    /// <summary>
    /// Gets all IdiotScripts for active setups.
    /// GET /api/breakoutsetups/scripts
    /// </summary>
    [HttpGet("scripts")]
    public IActionResult GetAllScripts()
    {
        var scripts = _setupService.GenerateIdiotScripts();
        return Ok(new
        {
            Count = scripts.Count,
            Scripts = scripts
        });
    }
    
    /// <summary>
    /// Gets all strategy cards for active setups.
    /// GET /api/breakoutsetups/cards
    /// </summary>
    [HttpGet("cards")]
    public IActionResult GetAllCards()
    {
        var cards = _setupService.GenerateStrategyCards();
        return Ok(new
        {
            Generated = DateTime.UtcNow,
            Content = cards
        });
    }
    
    /// <summary>
    /// Triggers a rescan of gappers for new setups.
    /// POST /api/breakoutsetups/rescan
    /// </summary>
    [HttpPost("rescan")]
    public IActionResult Rescan()
    {
        _setupService.RescanGappers();
        var setups = _setupService.GetActiveSetups();
        
        _logger.LogInformation("Rescan triggered - found {Count} setups", setups.Count);
        
        return Ok(new
        {
            Scanned = DateTime.UtcNow,
            SetupCount = setups.Count,
            Setups = setups.Select(s => new { s.Symbol, s.Bias, s.ConfidenceScore, State = s.State.ToString() })
        });
    }
    
    /// <summary>
    /// Gets confirmed setups ready for execution.
    /// GET /api/breakoutsetups/confirmed
    /// </summary>
    [HttpGet("confirmed")]
    public IActionResult GetConfirmedSetups()
    {
        var setups = _setupService.GetActiveSetups()
            .Where(s => s.State == SetupState.Confirmed)
            .ToList();
        
        return Ok(new
        {
            Count = setups.Count,
            Setups = setups.Select(s => new
            {
                s.Symbol,
                s.Bias,
                s.TriggerPrice,
                s.SupportPrice,
                s.InvalidationPrice,
                s.RiskRewardRatio,
                Targets = s.Targets.Select(t => new { t.Label, t.Price }),
                Script = s.ToIdiotScript()
            })
        });
    }
    
    /// <summary>
    /// Updates a setup's price (for testing without IBKR connection).
    /// POST /api/breakoutsetups/{symbol}/price
    /// </summary>
    [HttpPost("{symbol}/price")]
    public IActionResult UpdatePrice(string symbol, [FromBody] PriceUpdateRequest request)
    {
        _setupService.UpdatePrice(symbol, request.Price, request.Vwap);

        var setup = _setupService.GetSetup(symbol);
        return Ok(new
        {
            setup?.Symbol,
            State = setup?.State.ToString(),
            setup?.CurrentPrice,
            setup?.VwapPrice
        });
    }

    /// <summary>
    /// Gets statistics about current setups.
    /// GET /api/breakoutsetups/stats
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStatistics()
    {
        var stats = _setupService.GetStatistics();
        return Ok(stats);
    }

    /// <summary>
    /// Gets historical performance statistics.
    /// GET /api/breakoutsetups/performance?days=30
    /// </summary>
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] int days = 30)
    {
        var stats = await _setupService.GetPerformanceStatsAsync(days);
        if (stats == null)
        {
            return Ok(new { Message = "Performance tracking not enabled" });
        }

        return Ok(stats);
    }

    /// <summary>
    /// Marks a setup as entered (trade executed).
    /// POST /api/breakoutsetups/{symbol}/enter
    /// </summary>
    [HttpPost("{symbol}/enter")]
    public IActionResult MarkAsEntered(string symbol, [FromBody] EnterSetupRequest request)
    {
        var success = _setupService.MarkAsEntered(symbol, request.EntryPrice);
        if (!success)
        {
            return BadRequest($"Failed to mark {symbol} as entered. Setup may not exist or is not confirmed.");
        }

        _logger.LogInformation("Setup {Symbol} marked as entered at ${Price}", symbol, request.EntryPrice);

        var setup = _setupService.GetSetup(symbol);
        return Ok(new
        {
            setup?.Symbol,
            State = setup?.State.ToString(),
            setup?.ActualEntryPrice,
            setup?.EnteredUtc
        });
    }

    /// <summary>
    /// Marks a setup as completed.
    /// POST /api/breakoutsetups/{symbol}/complete
    /// </summary>
    [HttpPost("{symbol}/complete")]
    public IActionResult MarkAsCompleted(string symbol, [FromBody] CompleteSetupRequest request)
    {
        var success = _setupService.MarkAsCompleted(symbol, request.ExitPrice, request.Reason);
        if (!success)
        {
            return BadRequest($"Failed to mark {symbol} as completed.");
        }

        _logger.LogInformation("Setup {Symbol} completed at ${Price} - {Reason}", 
            symbol, request.ExitPrice, request.Reason);

        var setup = _setupService.GetSetup(symbol);
        return Ok(new
        {
            setup?.Symbol,
            State = setup?.State.ToString(),
            setup?.ActualEntryPrice,
            setup?.ActualExitPrice,
            setup?.CompletionReason,
            PnLPercent = setup != null && setup.ActualEntryPrice > 0 
                ? (setup.ActualExitPrice - setup.ActualEntryPrice) / setup.ActualEntryPrice * 100 
                : 0
        });
    }

    /// <summary>
    /// Backtests a setup against historical data.
    /// POST /api/breakoutsetups/{symbol}/backtest
    /// </summary>
    [HttpPost("{symbol}/backtest")]
    public async Task<IActionResult> BacktestSetup(
        string symbol, 
        [FromBody] BacktestRequest request,
        [FromServices] BreakoutBacktester backtester)
    {
        var setup = _setupService.GetSetup(symbol);
        if (setup == null)
        {
            return NotFound($"No setup found for {symbol}");
        }

        var startDate = request.StartDate ?? DateTime.UtcNow.AddDays(-30);
        var endDate = request.EndDate ?? DateTime.UtcNow;
        var config = new BacktestConfig
        {
            PositionSize = request.PositionSize,
            MoveStopToBreakevenAfterT1 = request.MoveStopToBreakevenAfterT1,
            MoveStopToT1AfterT2 = request.MoveStopToT1AfterT2,
            MaxHoldMinutes = request.MaxHoldMinutes
        };

        var result = await backtester.BacktestSetupAsync(setup, startDate, endDate, config);

        return Ok(result);
    }

    /// <summary>
    /// Backtests all active setups.
    /// POST /api/breakoutsetups/backtest-all
    /// </summary>
    [HttpPost("backtest-all")]
    public async Task<IActionResult> BacktestAllSetups(
        [FromBody] BacktestRequest request,
        [FromServices] BreakoutBacktester backtester)
    {
        var setups = _setupService.GetActiveSetups();
        if (setups.Count == 0)
        {
            return Ok(new { Message = "No active setups to backtest" });
        }

        var startDate = request.StartDate ?? DateTime.UtcNow.AddDays(-30);
        var endDate = request.EndDate ?? DateTime.UtcNow;
        var config = new BacktestConfig
        {
            PositionSize = request.PositionSize,
            MoveStopToBreakevenAfterT1 = request.MoveStopToBreakevenAfterT1,
            MoveStopToT1AfterT2 = request.MoveStopToT1AfterT2,
            MaxHoldMinutes = request.MaxHoldMinutes
        };

        var result = await backtester.BacktestMultipleAsync(setups, startDate, endDate, config);

        return Ok(result);
    }
}

public sealed class PriceUpdateRequest
{
    public double Price { get; set; }
    public double Vwap { get; set; }
}

public sealed class EnterSetupRequest
{
    public double EntryPrice { get; set; }
}

public sealed class CompleteSetupRequest
{
    public double ExitPrice { get; set; }
    public string Reason { get; set; } = "Manual close";
}

public sealed class BacktestRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PositionSize { get; set; } = 100;
    public bool MoveStopToBreakevenAfterT1 { get; set; } = true;
    public bool MoveStopToT1AfterT2 { get; set; } = true;
    public int MaxHoldMinutes { get; set; } = 0;
}
