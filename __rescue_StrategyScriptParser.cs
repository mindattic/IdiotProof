// ============================================================================
// StrategyScriptParser - Wrapper for IdiotScript parser
// ============================================================================
//
// This is now a thin wrapper around the shared IdiotScriptParser.
// The full parser implementation is in IdiotProof.Shared.Scripting.
//
// IDIOTSCRIPT SYNTAX:
// Commands use period (.) as the universal delimiter. Commands are case-insensitive.
// Scripts are auto-converted to PascalCase before validation.
// Constants use IS. prefix (e.g., IS.PREMARKET, IS.BELL, IS.MODERATE).
//
// SCRIPT STRUCTURE:
// A script starts with a symbol declaration and chains commands with periods:
// - Valid starts: Ticker(AAPL), Sym(AAPL), Symbol(AAPL), Stock.Ticker(AAPL), Stock.Symbol(AAPL)
//
// AVAILABLE COMMANDS (PascalCase format):
// - Ticker(AAPL) or Sym(AAPL) or Symbol(AAPL) - Set the stock symbol (required)
// - Qty(100)                       - Set quantity to buy/sell
// - Entry(148.75) or Price(148.75) - Entry price condition
// - IsPriceAbove(150)              - Price above level condition
// - IsPriceBelow(140)              - Price below level condition
// - TakeProfit(158) or TP($158)    - Take profit target
// - StopLoss(145) or SL($145)      - Stop loss price
// - TrailingStopLoss(15) or TSL(IS.MODERATE) - Trailing stop loss percentage
// - AboveVwap or IsAboveVwap       - Above VWAP condition
// - BelowVwap or IsBelowVwap       - Below VWAP condition
// - EmaAbove(9) or IsEmaAbove(9)   - Price above EMA condition
// - EmaBelow(9) or IsEmaBelow(9)   - Price below EMA condition
// - EmaBetween(9, 21)              - Price between two EMAs
// - RsiOversold(30)                - RSI oversold condition
// - RsiOverbought(70)              - RSI overbought condition
// - AdxAbove(25)                   - ADX above threshold
// - MacdBullish or IsMacdBullish   - MACD bullish crossover
// - MacdBearish or IsMacdBearish   - MACD bearish crossover
// - DiPositive or IsDiPositive     - +DI above threshold
// - DiNegative or IsDiNegative     - -DI above threshold
// - Session(IS.PREMARKET)          - Set trading session
// - ClosePosition(IS.BELL)         - Close position time
// - Buy or Sell                    - Order direction (default: Buy)
// - CloseLong                      - Close a long position
// - CloseShort                     - Close a short position  
// - Name("My Strategy")            - Strategy name
// - TimeInForce(DAY)               - Order time-in-force
// - OutsideRTH(true)               - Allow extended hours execution
// - AllOrNone(true)                - Require full fill or cancel
// - OrderType(LIMIT)               - Set order type
//
// EXAMPLE:
// Ticker(NVDA).Session(IS.PREMARKET).ClosePosition(IS.PREMARKET.BELL).Qty(1).Entry(200).TakeProfit(201).StopLoss(190).TrailingStopLoss(10).Breakout().Pullback().IsAboveVwap().EmaBetween(9, 21).EmaAbove(200)
//
// ============================================================================

using IdiotProof.Shared.Models;
using IdiotProof.Shared.Scripting;

namespace IdiotProof.Console.Scripting;

/// <summary>
/// Parses IdiotScript strings into StrategyDefinition objects.
/// This is a wrapper around the shared IdiotScriptParser.
/// </summary>
public static class StrategyScriptParser
{
    /// <summary>
    /// Parses an IdiotScript string into a StrategyDefinition.
    /// </summary>
    /// <param name="script">The script string to parse.</param>
    /// <param name="defaultSymbol">Default symbol if not specified in script.</param>
    /// <returns>A parsed StrategyDefinition.</returns>
    /// <exception cref="StrategyScriptException">Thrown when parsing fails.</exception>
    public static StrategyDefinition Parse(string script, string? defaultSymbol = null)
    {
        try
        {
            return IdiotScriptParser.Parse(script, defaultSymbol);
        }
        catch (IdiotScriptException ex)
        {
            throw new StrategyScriptException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Tries to parse an IdiotScript string.
    /// </summary>
    public static bool TryParse(string script, out StrategyDefinition? strategy, out string? error, string? defaultSymbol = null)
    {
        return IdiotScriptParser.TryParse(script, out strategy, out error, defaultSymbol);
    }

    /// <summary>
    /// Validates an IdiotScript without fully parsing it.
    /// </summary>
    public static (bool IsValid, List<string> Errors) Validate(string script)
    {
        return IdiotScriptParser.Validate(script);
    }

    /// <summary>
    /// Converts a StrategyDefinition to an IdiotScript string.
    /// Returns the original script if available, otherwise re-serializes.
    /// </summary>
    public static string ToScript(StrategyDefinition strategy)
    {
        return strategy.ToIdiotScript();
    }
}

/// <summary>
/// Exception thrown when parsing an IdiotScript fails.
/// </summary>
public class StrategyScriptException : Exception
{
    public StrategyScriptException(string message) : base(message) { }
    public StrategyScriptException(string message, Exception inner) : base(message, inner) { }
}
