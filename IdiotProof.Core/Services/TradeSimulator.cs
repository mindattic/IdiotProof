// ============================================================================
// Trade Simulator - Backtest trading decisions
// ============================================================================
//
// Simulates trading strategies against historical data.
// Calculates market scores using MarketScoreCalculator (same as live trading)
// and shows what trades would have been executed with full reasoning.
//
// Can optionally use HistoricalMetadata to make smarter decisions based on
// how the stock typically behaves (HOD/LOD patterns, support/resistance, etc.)
//
// ============================================================================

using IdiotProof.Analysis;
using IdiotProof.Calculators;
using IdiotProof.Constants;
using IdiotProof.Helpers;
using IdiotProof.Learning;
using IdiotProof.Models;
using IdiotProof.Settings;
using IdiotProof.Strategy;

namespace IdiotProof.Services;

/// <summary>
/// Configuration for autonomous trading simulation.
/// </summary>
public sealed record AutonomousConfig
{
    /// <summary>Base entry threshold - adjusted dynamically based on conditions.</summary>
    public int BaseEntryThreshold { get; init; } = TradingDefaults.LongEntryThreshold;

    /// <summary>Position size in shares.</summary>
    public int Quantity { get; init; } = 100;

    /// <summary>Allow short positions.</summary>
    public bool AllowShort { get; init; } = false;

    /// <summary>Allow flipping from long to short (and vice versa).</summary>
    public bool AllowDirectionFlip { get; init; } = false;

    /// <summary>Minimum seconds between trades.</summary>
    public int MinSecondsBetweenTrades { get; init; } = TradingDefaults.MinSecondsBetweenAdjustments;

    /// <summary>ATR multiplier for take profit.</summary>
    public double TakeProfitAtrMultiplier { get; init; } = TradingDefaults.TpAtrMultiplier;

    /// <summary>ATR multiplier for stop loss.</summary>
    public double StopLossAtrMultiplier { get; init; } = TradingDefaults.SlAtrMultiplier;

    /// <summary>Exit at end of day.</summary>
    public bool ExitAtEod { get; init; } = true;

    /// <summary>EOD exit time.</summary>
    public TimeOnly EodExitTime { get; init; } = new TimeOnly(15, 55);

    // ========================================================================
    // Base Thresholds - Adjusted dynamically per bar based on conditions
    // ========================================================================

    /// <summary>Base long entry threshold.</summary>
    public int LongEntryThreshold => BaseEntryThreshold;

    /// <summary>Base short entry threshold.</summary>
    public int ShortEntryThreshold => -BaseEntryThreshold;

    /// <summary>Exit threshold - momentum fading.</summary>
    public int LongExitThreshold => (int)(BaseEntryThreshold * 0.6);  // 40 when base=65 (matches LIVE)

    /// <summary>Exit threshold - momentum fading.</summary>
    public int ShortExitThreshold => -(int)(BaseEntryThreshold * 0.6);  // -40 when base=65 (matches LIVE)
}

/// <summary>
/// Detailed breakdown of how the market score was calculated.
/// </summary>
public sealed record ScoreBreakdown
{
    public double VwapScore { get; init; }
    public double EmaScore { get; init; }
    public double RsiScore { get; init; }
    public double MacdScore { get; init; }
    public double AdxScore { get; init; }
    public double VolumeScore { get; init; }
    public double TotalScore { get; init; }

    public override string ToString() =>
        $"VWAP:{VwapScore:+0;-0;0} EMA:{EmaScore:+0;-0;0} RSI:{RsiScore:+0;-0;0} " +
        $"MACD:{MacdScore:+0;-0;0} ADX:{AdxScore:+0;-0;0} VOL:{VolumeScore:+0;-0;0} = {TotalScore:+0;-0;0}";
}

/// <summary>
/// A single autonomous trade with full details.
/// </summary>
public sealed record AutonomousTrade
{
    public required DateTime EntryTime { get; init; }
    public required DateTime ExitTime { get; init; }
    public required double EntryPrice { get; init; }
    public required double ExitPrice { get; init; }
    public required int Quantity { get; init; }
    public required bool IsLong { get; init; }
    public required string EntryReason { get; init; }
    public required string ExitReason { get; init; }
    public required ScoreBreakdown EntryScore { get; init; }
    public required ScoreBreakdown ExitScore { get; init; }

    public double PnL => IsLong
        ? (ExitPrice - EntryPrice) * Quantity
        : (EntryPrice - ExitPrice) * Quantity;

    public double PnLPercent => IsLong
        ? (ExitPrice - EntryPrice) / EntryPrice * 100
        : (EntryPrice - ExitPrice) / EntryPrice * 100;

    public TimeSpan Duration => ExitTime - EntryTime;

    public override string ToString() =>
        $"{EntryTime:HH:mm} -> {ExitTime:HH:mm} | " +
        $"{(IsLong ? "LONG " : "SHORT")} | " +
        $"${EntryPrice:F2} -> ${ExitPrice:F2} | " +
        $"PnL: {(PnL >= 0 ? "+" : "")}{PnL:F2} ({PnLPercent:+0.0;-0.0}%)";
}

/// <summary>
/// Complete result of an autonomous trading simulation.
/// </summary>
public sealed class AutonomousSimulationResult
{
    public required string Symbol { get; init; }
    public required DateOnly Date { get; init; }
    public required AutonomousConfig Config { get; init; }
    public List<AutonomousTrade> Trades { get; init; } = [];

    // Score tracking
    public List<(DateTime Time, ScoreBreakdown Score)> ScoreHistory { get; init; } = [];

    // ========================================================================
    // Performance Metrics
    // ========================================================================

    public double TotalPnL => Trades.Sum(t => t.PnL);
    public double TotalPnLPercent => Trades.Sum(t => t.PnLPercent);
    public int WinCount => Trades.Count(t => t.PnL > 0);
    public int LossCount => Trades.Count(t => t.PnL < 0);
    public double WinRate => Trades.Count > 0 ? (double)WinCount / Trades.Count * 100 : 0;

    public double AvgWin => WinCount > 0
        ? Trades.Where(t => t.PnL > 0).Average(t => t.PnL)
        : 0;

    public double AvgLoss => LossCount > 0
        ? Trades.Where(t => t.PnL < 0).Average(t => t.PnL)
        : 0;

    public double ProfitFactor
    {
        get
        {
            var grossProfit = Trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
            var grossLoss = Math.Abs(Trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
            return grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.MaxValue : 0;
        }
    }

    public double MaxDrawdown { get; set; }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"""
            +==================================================================+
            | AUTONOMOUS TRADING SIMULATION                                    |
            +==================================================================+
            | Symbol: {Symbol,-10} | Date: {Date:yyyy-MM-dd}
            | Thresh: {Config.BaseEntryThreshold,-10} | Qty: {Config.Quantity}
            +------------------------------------------------------------------+
            | PERFORMANCE                                                      |
            +------------------------------------------------------------------+
            | Trades:      {Trades.Count,6}
            | Win Rate:    {WinRate,5:F1}%
            | Total PnL:  ${TotalPnL,8:F2}
            | Avg Win:    ${AvgWin,8:F2}
            | Avg Loss:   ${AvgLoss,8:F2}
            | Profit Factor: {ProfitFactor,5:F2}
            | Max Drawdown: ${MaxDrawdown,7:F2}
            +------------------------------------------------------------------+
            """);

        if (Trades.Count > 0)
        {
            sb.AppendLine("| TRADES                                                           |");
            sb.AppendLine("+------------------------------------------------------------------+");

            foreach (var trade in Trades)
            {
                sb.AppendLine($"| {trade}");
                sb.AppendLine($"|   Entry: {trade.EntryReason}");
                sb.AppendLine($"|   Exit:  {trade.ExitReason}");
                sb.AppendLine($"|   Scores: Entry={trade.EntryScore.TotalScore:+0;-0} Exit={trade.ExitScore.TotalScore:+0;-0}");
                sb.AppendLine("|");
            }
        }

        sb.AppendLine("+==================================================================+");
        return sb.ToString();
    }
}

/// <summary>
/// Simulates trading strategies against historical data.
/// </summary>
public sealed class TradeSimulator
{
    private readonly BackTestSession session;
    private readonly IndicatorCalculator indicators;
    private readonly AutonomousConfig config;

    // Historical metadata for smarter decisions (optional)
    private readonly HistoricalMetadata? metadata;
    private readonly TickerProfile? profile;
    
    // Indicator weights - learned or default
    private IdiotProof.Optimization.IndicatorWeights weights = IdiotProof.Optimization.IndicatorWeights.Default;

    // Cached indicator values
    private double[]? ema9;
    private double[]? ema21;
    private double[]? ema34;
    private double[]? ema50;
    private double[]? rsi;
    private (double[] adx, double[] plusDi, double[] minusDi)? adxData;
    private (double[] macd, double[] signal, double[] histogram)? macdData;
    private double[]? volumeRatio;
    private double[]? atr;
    
    // Extended indicators (must match LIVE for score parity)
    private (double[] upper, double[] middle, double[] lower, double[] percentB, double[] bandwidth)? bollingerData;
    private (double[] k, double[] d)? stochasticData;
    private double[]? obvSlope;
    private double[]? cci;
    private double[]? williamsR;
    private double[]? sma20;
    private double[]? sma50;
    private double[]? momentum;
    private double[]? roc;

    public TradeSimulator(BackTestSession session, AutonomousConfig? config = null)
    {
        this.session = session;
        this.config = config ?? new AutonomousConfig();
        indicators = new IndicatorCalculator(session.Candles);
    }

    /// <summary>
    /// Creates a simulator with historical metadata for smarter decisions.
    /// </summary>
    public TradeSimulator(
        BackTestSession session,
        AutonomousConfig? config,
        HistoricalMetadata? metadata,
        TickerProfile? profile = null)
        : this(session, config)
    {
        this.metadata = metadata;
        this.profile = profile;
    }

    /// <summary>
    /// Gets whether historical metadata is available.
    /// </summary>
    public bool HasHistoricalMetadata => metadata != null;
    
    /// <summary>
    /// Sets the indicator weights for score calculation.
    /// Call after construction to use ticker-specific learned weights.
    /// </summary>
    public void SetWeights(IdiotProof.Optimization.IndicatorWeights weights)
    {
        this.weights = weights;
    }

    /// <summary>
    /// Runs the autonomous trading simulation.
    /// </summary>
    public AutonomousSimulationResult Simulate(IProgress<string>? progress = null)
    {
        var result = new AutonomousSimulationResult
        {
            Symbol = session.Symbol,
            Date = session.Date,
            Config = config
        };

        var candles = session.Candles;
        if (candles.Count == 0)
            return result;

        // Pre-calculate all indicators
        progress?.Report("Calculating indicators...");
        CalculateAllIndicators();

        // Minimum warmup period for indicators
        int warmupPeriod = 50;  // Need enough bars for ADX to be valid

        bool inPosition = false;
        bool isLong = false;
        double entryPrice = 0;
        DateTime entryTime = default;
        ScoreBreakdown entryScore = default!;
        string entryReason = "";
        DateTime lastTradeTime = DateTime.MinValue;
        double takeProfitPrice = 0;
        double stopLossPrice = 0;


        progress?.Report("Running simulation...");

        for (int i = warmupPeriod; i < candles.Count; i++)
        {
            var candle = candles[i];
            var time = TimeOnly.FromDateTime(candle.Timestamp);
            var score = CalculateMarketScore(i);

            // Track score history
            result.ScoreHistory.Add((candle.Timestamp, score));

            // Check for EOD exit
            if (inPosition && config.ExitAtEod && time >= config.EodExitTime)
            {
                result.Trades.Add(new AutonomousTrade
                {
                    EntryTime = entryTime,
                    ExitTime = candle.Timestamp,
                    EntryPrice = entryPrice,
                    ExitPrice = candle.Close,
                    Quantity = config.Quantity,
                    IsLong = isLong,
                    EntryReason = entryReason,
                    ExitReason = "End of day exit",
                    EntryScore = entryScore,
                    ExitScore = score
                });
                inPosition = false;
                continue;
            }

            if (!inPosition)
            {
                // Check for entry signals
                bool canTrade = (candle.Timestamp - lastTradeTime).TotalSeconds >= config.MinSecondsBetweenTrades;
                
                // Opening bell protection (matches LIVE: block 9:30-9:32 entries)
                if (canTrade && time >= new TimeOnly(9, 30) && time < new TimeOnly(9, 32))
                    canTrade = false;

                if (canTrade)
                {
                    // Apply realistic slippage based on price tier (penny stocks have wide spreads)
                    double slippagePct = TradingDefaults.GetSlippagePercent(candle.Close);
                    
                    // Long entry
                    if (score.TotalScore >= config.LongEntryThreshold)
                    {
                        inPosition = true;
                        isLong = true;
                        entryPrice = candle.Close * (1 + slippagePct); // Buy at slightly higher price
                        entryTime = candle.Timestamp;
                        entryScore = score;
                        entryReason = $"Score {score.TotalScore:+0} >= {config.LongEntryThreshold} (LONG threshold)";
                        lastTradeTime = candle.Timestamp;

                        // Calculate TP/SL based on ATR
                        double atrVal = atr![i];
                        takeProfitPrice = entryPrice + (atrVal * config.TakeProfitAtrMultiplier);
                        stopLossPrice = entryPrice - (atrVal * config.StopLossAtrMultiplier);
                    }
                    // Short entry
                    else if (config.AllowShort && score.TotalScore <= config.ShortEntryThreshold)
                    {
                        inPosition = true;
                        isLong = false;
                        entryPrice = candle.Close * (1 - slippagePct); // Sell at slightly lower price
                        entryTime = candle.Timestamp;
                        entryScore = score;
                        entryReason = $"Score {score.TotalScore:+0} <= {config.ShortEntryThreshold} (SHORT threshold)";
                        lastTradeTime = candle.Timestamp;

                        // Calculate TP/SL based on ATR (inverted for short)
                        double atrVal = atr![i];
                        takeProfitPrice = entryPrice - (atrVal * config.TakeProfitAtrMultiplier);
                        stopLossPrice = entryPrice + (atrVal * config.StopLossAtrMultiplier);
                    }
                }
            }
            else
            {
                // Check exit conditions
                string? exitReason = null;
                double exitPrice = candle.Close;

                // Apply slippage to score-based exits (TP/SL exits use the fixed price)
                double exitSlippage = TradingDefaults.GetSlippagePercent(candle.Close);

                if (isLong)
                {
                    // Take profit
                    if (candle.High >= takeProfitPrice)
                    {
                        exitReason = $"Take profit hit at ${takeProfitPrice:F2}";
                        exitPrice = takeProfitPrice;
                    }
                    // Stop loss (only if enabled in settings)
                    else if (AppSettings.UseStopLoss && candle.Low <= stopLossPrice)
                    {
                        exitReason = $"Stop loss hit at ${stopLossPrice:F2}";
                        exitPrice = stopLossPrice;
                    }
                    // Score-based exit
                    else if (score.TotalScore < config.LongExitThreshold)
                    {
                        exitReason = $"Score {score.TotalScore:+0} < {config.LongExitThreshold} (momentum fading)";
                        exitPrice = candle.Close * (1 - exitSlippage); // Selling long at slightly lower
                    }
                    // Emergency exit on strong bearish signal
                    else if (score.TotalScore <= -70)
                    {
                        exitReason = $"Emergency exit - strong bearish signal ({score.TotalScore:+0})";
                        exitPrice = candle.Close * (1 - exitSlippage); // Selling long at slightly lower
                    }
                }
                else  // Short position
                {
                    // Take profit
                    if (candle.Low <= takeProfitPrice)
                    {
                        exitReason = $"Take profit hit at ${takeProfitPrice:F2}";
                        exitPrice = takeProfitPrice;
                    }
                    // Stop loss (only if enabled in settings)
                    else if (AppSettings.UseStopLoss && candle.High >= stopLossPrice)
                    {
                        exitReason = $"Stop loss hit at ${stopLossPrice:F2}";
                        exitPrice = stopLossPrice;
                    }
                    // Score-based exit
                    else if (score.TotalScore > config.ShortExitThreshold)
                    {
                        exitReason = $"Score {score.TotalScore:+0} > {config.ShortExitThreshold} (momentum fading)";
                        exitPrice = candle.Close * (1 + exitSlippage); // Covering short at slightly higher
                    }
                    // Emergency exit on strong bullish signal
                    else if (score.TotalScore >= 70)
                    {
                        exitReason = $"Emergency exit - strong bullish signal ({score.TotalScore:+0})";
                        exitPrice = candle.Close * (1 + exitSlippage); // Covering short at slightly higher
                    }
                }

                if (exitReason != null)
                {
                    var trade = new AutonomousTrade
                    {
                        EntryTime = entryTime,
                        ExitTime = candle.Timestamp,
                        EntryPrice = entryPrice,
                        ExitPrice = exitPrice,
                        Quantity = config.Quantity,
                        IsLong = isLong,
                        EntryReason = entryReason,
                        ExitReason = exitReason,
                        EntryScore = entryScore,
                        ExitScore = score
                    };
                    result.Trades.Add(trade);

                    inPosition = false;

                    // Check for direction flip
                    if (config.AllowDirectionFlip)
                    {
                        double flipSlippage = TradingDefaults.GetSlippagePercent(candle.Close);
                        
                        if (isLong && score.TotalScore <= config.ShortEntryThreshold && config.AllowShort)
                        {
                            // Flip to short
                            inPosition = true;
                            isLong = false;
                            entryPrice = candle.Close * (1 - flipSlippage); // Sell at slightly lower
                            entryTime = candle.Timestamp;
                            entryScore = score;
                            entryReason = $"Direction flip: Score {score.TotalScore:+0} - going SHORT";
                            lastTradeTime = candle.Timestamp;

                            double atrVal = atr![i];
                            takeProfitPrice = entryPrice - (atrVal * config.TakeProfitAtrMultiplier);
                            stopLossPrice = entryPrice + (atrVal * config.StopLossAtrMultiplier);
                        }
                        else if (!isLong && score.TotalScore >= config.LongEntryThreshold)
                        {
                            // Flip to long
                            inPosition = true;
                            isLong = true;
                            entryPrice = candle.Close * (1 + flipSlippage); // Buy at slightly higher
                            entryTime = candle.Timestamp;
                            entryScore = score;
                            entryReason = $"Direction flip: Score {score.TotalScore:+0} - going LONG";
                            lastTradeTime = candle.Timestamp;

                            double atrVal = atr![i];
                            takeProfitPrice = entryPrice + (atrVal * config.TakeProfitAtrMultiplier);
                            stopLossPrice = entryPrice - (atrVal * config.StopLossAtrMultiplier);
                        }
                    }
                }
            }
        }

        // Close any open position at end
        if (inPosition && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            var finalScore = CalculateMarketScore(candles.Count - 1);

            result.Trades.Add(new AutonomousTrade
            {
                EntryTime = entryTime,
                ExitTime = lastCandle.Timestamp,
                EntryPrice = entryPrice,
                ExitPrice = lastCandle.Close,
                Quantity = config.Quantity,
                IsLong = isLong,
                EntryReason = entryReason,
                ExitReason = "End of data",
                EntryScore = entryScore,
                ExitScore = finalScore
            });
        }

        // Calculate max drawdown
        CalculateDrawdown(result);

        progress?.Report($"Simulation complete. {result.Trades.Count} trades executed.");
        return result;
    }

    private void CalculateAllIndicators()
    {
        ema9 = indicators.CalculateEma(9);
        ema21 = indicators.CalculateEma(21);
        ema34 = indicators.CalculateEma(34);  // PRIMARY: Key decision level for trading rules
        ema50 = indicators.CalculateEma(50);
        rsi = indicators.CalculateRsi(14);
        adxData = indicators.CalculateAdx(14);
        macdData = indicators.CalculateMacd(12, 26, 9);
        volumeRatio = indicators.CalculateVolumeRatio(20);
        atr = indicators.CalculateAtr(14);
        
        // Extended indicators (must match LIVE for score parity)
        bollingerData = indicators.CalculateBollingerBands(20, 2.0);
        stochasticData = indicators.CalculateStochastic(14, 3);
        obvSlope = indicators.CalculateObvSlope(20);
        cci = indicators.CalculateCci(20);
        williamsR = indicators.CalculateWilliamsR(14);
        sma20 = indicators.CalculateSma(20);
        sma50 = indicators.CalculateSma(50);
        momentum = indicators.CalculateMomentum(10);
        roc = indicators.CalculateRoc(10);
    }

    /// <summary>
    /// Calculates the market score at a given candle index.
    /// Score ranges from -100 to +100.
    /// </summary>
    private ScoreBreakdown CalculateMarketScore(int index)
    {
        var candle = session.Candles[index];

        // Create snapshot for MarketScoreCalculator (SINGLE SOURCE OF TRUTH)
        var snapshot = new IndicatorSnapshot
        {
            Price = candle.Close,
            Vwap = candle.Vwap,
            Ema9 = ema9![index],
            Ema21 = ema21![index],
            Ema34 = ema34![index],
            Ema50 = ema50![index],
            Rsi = rsi![index],
            Macd = macdData!.Value.macd[index],
            MacdSignal = macdData.Value.signal[index],
            MacdHistogram = macdData.Value.histogram[index],
            Adx = adxData!.Value.adx[index],
            PlusDi = adxData.Value.plusDi[index],
            MinusDi = adxData.Value.minusDi[index],
            VolumeRatio = volumeRatio![index],
            Atr = atr![index],
            // Extended indicators - real values matching LIVE
            BollingerUpper = bollingerData!.Value.upper[index],
            BollingerMiddle = bollingerData.Value.middle[index],
            BollingerLower = bollingerData.Value.lower[index],
            StochasticK = stochasticData!.Value.k[index],
            StochasticD = stochasticData.Value.d[index],
            ObvSlope = obvSlope![index],
            Cci = cci![index],
            WilliamsR = williamsR![index],
            Sma20 = sma20![index],
            Sma50 = sma50![index],
            Momentum = momentum![index],
            Roc = roc![index],
            BollingerPercentB = bollingerData.Value.percentB[index],
            BollingerBandwidth = bollingerData.Value.bandwidth[index]
        };

        var result = MarketScoreCalculator.Calculate(snapshot, weights.ToCalculatorWeights());

        // Convert to ScoreBreakdown for display compatibility
        // Use raw component scores (weights already applied by MarketScoreCalculator)
        return new ScoreBreakdown
        {
            VwapScore = result.VwapScore,
            EmaScore = result.EmaScore,
            RsiScore = result.RsiScore,
            MacdScore = result.MacdScore,
            AdxScore = result.AdxScore,
            VolumeScore = result.VolumeScore,
            TotalScore = result.TotalScore
        };
    }

    /// <summary>
    /// Applies historical metadata adjustments to entry decisions.
    /// Returns an adjusted score and entry reason modification.
    /// </summary>
    private (double adjustedScore, string? metadataReason) ApplyMetadataAdjustments(
        double rawScore,
        int candleIndex,
        bool isLongEntry)
    {
        if (metadata == null) return (rawScore, null);

        var candle = session.Candles[candleIndex];
        var time = TimeOnly.FromDateTime(candle.Timestamp);
        int minutesFromOpen = (int)(candle.Timestamp.TimeOfDay - new TimeSpan(9, 30, 0)).TotalMinutes;
        if (minutesFromOpen < 0) minutesFromOpen = 0;

        double adjustment = 0;
        var reasons = new List<string>();

        // ====================================================================
        // HOD/LOD Timing Adjustments
        // ====================================================================
        if (isLongEntry)
        {
            // If HOD typically occurs in first 30 min and we're past that, reduce long confidence
            if (metadata.DailyExtremes.HodInFirst30MinPercent > 50 && minutesFromOpen > 30)
            {
                adjustment -= 10;
                reasons.Add("HOD typically early");
            }

            // If LOD typically occurs now, boost long entry (buying the dip)
            if (minutesFromOpen > 0 && 
                Math.Abs(minutesFromOpen - metadata.DailyExtremes.AvgLodMinutesFromOpen) < 30)
            {
                adjustment += 15;
                reasons.Add("near typical LOD time");
            }
        }
        else  // Short entry
        {
            // If LOD typically occurs in first 30 min and we're past that, reduce short confidence
            if (metadata.DailyExtremes.LodInFirst30MinPercent > 50 && minutesFromOpen > 30)
            {
                adjustment -= 10;
                reasons.Add("LOD typically early");
            }

            // If HOD typically occurs now, boost short entry (selling the top)
            if (minutesFromOpen > 0 &&
                Math.Abs(minutesFromOpen - metadata.DailyExtremes.AvgHodMinutesFromOpen) < 30)
            {
                adjustment += 15;
                reasons.Add("near typical HOD time");
            }
        }

        // ====================================================================
        // Support/Resistance Level Adjustments
        // ====================================================================
        double currentPrice = candle.Close;

        // Check if price is near a support level (good for longs)
        foreach (var support in metadata.SupportLevels.Where(s => s.Strength >= 0.6))
        {
            double distance = Math.Abs(currentPrice - support.Price) / support.Price * 100;
            if (distance < 0.5)  // Within 0.5% of support
            {
                if (isLongEntry)
                {
                    adjustment += 10 * support.Strength;
                    reasons.Add($"at support ${support.Price:F2}");
                }
                else
                {
                    adjustment -= 10 * support.Strength;
                    reasons.Add($"support at ${support.Price:F2}");
                }
                break;
            }
        }

        // Check if price is near a resistance level (good for shorts)
        foreach (var resistance in metadata.ResistanceLevels.Where(r => r.Strength >= 0.6))
        {
            double distance = Math.Abs(currentPrice - resistance.Price) / resistance.Price * 100;
            if (distance < 0.5)  // Within 0.5% of resistance
            {
                if (!isLongEntry)
                {
                    adjustment += 10 * resistance.Strength;
                    reasons.Add($"at resistance ${resistance.Price:F2}");
                }
                else
                {
                    adjustment -= 10 * resistance.Strength;
                    reasons.Add($"resistance at ${resistance.Price:F2}");
                }
                break;
            }
        }

        // ====================================================================
        // Gap Behavior Adjustments
        // ====================================================================
        // Note: Would need previous close data to properly detect gaps
        // This is a placeholder for future enhancement

        // ====================================================================
        // Profile-Based Adjustments (from learned trading patterns)
        // ====================================================================
        if (profile != null)
        {
            // Avoid hours with historically poor performance
            if (profile.ShouldAvoidHour(time.Hour))
            {
                adjustment -= 15;
                reasons.Add($"avoid {time.Hour}:00 (low win rate)");
            }

            // Boost during best hours
            if (profile.BestHours.Contains(time.Hour))
            {
                adjustment += 10;
                reasons.Add($"best hour {time.Hour}:00");
            }

            // Adjust for streaks
            double streakMultiplier = profile.GetStreakRiskMultiplier();
            if (streakMultiplier < 1.0)
            {
                adjustment *= streakMultiplier;
                reasons.Add($"loss streak caution");
            }
        }

        // Apply adjustments with limits
        double adjustedScore = rawScore;
        if (isLongEntry)
        {
            adjustedScore = rawScore + adjustment;
        }
        else
        {
            adjustedScore = rawScore - adjustment;  // For shorts, adjustment affects negative scores
        }

        string? metadataReason = reasons.Count > 0 
            ? $"[Metadata: {string.Join(", ", reasons)}]" 
            : null;

        return (adjustedScore, metadataReason);
    }

    private static void CalculateDrawdown(AutonomousSimulationResult result)
    {
        if (result.Trades.Count == 0) return;

        double peak = 0;
        double maxDrawdown = 0;
        double runningPnL = 0;

        foreach (var trade in result.Trades)
        {
            runningPnL += trade.PnL;

            if (runningPnL > peak)
                peak = runningPnL;

            double drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        result.MaxDrawdown = maxDrawdown;
    }

    /// <summary>
    /// Generates a score timeline for visualization.
    /// </summary>
    public string GenerateScoreTimeline(AutonomousSimulationResult result, int intervalMinutes = 30)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("+------------------------------------------------------------------+");
        sb.AppendLine("| MARKET SCORE TIMELINE                                            |");
        sb.AppendLine("+------------------------------------------------------------------+");

        var samples = result.ScoreHistory
            .Where((_, i) => i % intervalMinutes == 0)
            .Take(20)
            .ToList();

        foreach (var (time, score) in samples)
        {
            int barLength = (int)Math.Abs(score.TotalScore / 5);
            string bar = score.TotalScore >= 0
                ? new string('+', barLength)
                : new string('-', barLength);

            string signal = score.TotalScore >= config.LongEntryThreshold ? " -> LONG" :
                           score.TotalScore <= config.ShortEntryThreshold ? " -> SHORT" : "";

            sb.AppendLine($"| {time:HH:mm} | {score.TotalScore,6:+0;-0;0} | {bar,-20} {signal}");
        }

        sb.AppendLine("+------------------------------------------------------------------+");
        return sb.ToString();
    }
}
