// ============================================================================
// IdiotScriptParser - Universal parser for IdiotScript language
// ============================================================================
//
// IDIOTSCRIPT SYNTAX:
// Commands use period (.) as the universal delimiter. Commands are case-insensitive.
// Constants use IS. prefix (e.g., IS.PREMARKET, IS.BELL, IS.MODERATE).
// Scripts are auto-converted to PascalCase before validation.
//
// SCRIPT STRUCTURE:
// A script starts with a symbol declaration and chains commands with periods:
// - Valid starts: TICKER(AAPL), SYM(AAPL), SYMBOL(AAPL), STOCK.TICKER(AAPL), STOCK.SYMBOL(AAPL), STRATEGY.
//
// COMMAND CATEGORIES:
// 1. ORDER PART - Defines what to buy/sell (QTY, BUY/SELL, TP, SL, TSL, ENTRY)
// 2. STEPS PART - Defines entry conditions (BREAKOUT, PULLBACK, VWAP, EMA, RSI, ADX, MACD, DI)
// 3. SESSION PART - Defines timing (SESSION, CLOSEPOSITION)
// 4. ORDER CONFIG - Order settings (TIMEINFORCE, OUTSIDERTH, ALLORNONE, ORDERTYPE)
//
// AVAILABLE COMMANDS:
// - TICKER(AAPL) or SYM(AAPL) or SYMBOL(AAPL) - Set the stock symbol (required)
// - STOCK.TICKER(AAPL) or STOCK.SYMBOL(AAPL)  - Alternative symbol syntax
// - NAME("My Strategy")           - Strategy name
// - DESC("Description")           - Strategy description
// - QTY(100)                      - Set quantity to buy/sell
// - ENTRY(148.75) or PRICE(148.75)- Entry price condition
// - IsPriceAbove(150)             - Price above level condition
// - IsPriceBelow(140)             - Price below level condition
// - TP(158) or TakeProfit($158)   - Take profit target
// - SL(145) or StopLoss($145)     - Stop loss price
// - TSL(15) or TrailingStopLoss(IS.MODERATE) - Trailing stop loss percentage
// - AboveVwap or IsAboveVwap      - Above VWAP condition
// - BelowVwap or IsBelowVwap      - Below VWAP condition
// - EmaAbove(9) or IsEmaAbove(9)  - Price above EMA condition
// - EmaBelow(9) or IsEmaBelow(9)  - Price below EMA condition
// - EmaBetween(9, 21) or IsEmaBetween(9, 21) - Price between two EMAs
// - RsiOversold(30) or IsRsiOversold(30) - RSI oversold condition
// - RsiOverbought(70) or IsRsiOverbought(70) - RSI overbought condition
// - AdxAbove(25) or IsAdxAbove(25)- ADX above threshold
// - MacdBullish or IsMacdBullish  - MACD bullish crossover
// - MacdBearish or IsMacdBearish  - MACD bearish crossover
// - DiPositive or IsDiPositive    - +DI above threshold
// - DiNegative or IsDiNegative    - -DI above threshold
// - MomentumAbove(0) or IsMomentumAbove(0) - Momentum above threshold (upward momentum)
// - MomentumBelow(0) or IsMomentumBelow(0) - Momentum below threshold (downward momentum)
// - RocAbove(2) or IsRocAbove(2)  - Rate of Change above threshold (positive momentum)
// - RocBelow(-2) or IsRocBelow(-2)- Rate of Change below threshold (negative momentum)
// - BREAKOUT() or BREAKOUT(150)   - Breakout condition
// - PULLBACK() or PULLBACK(148)   - Pullback condition
// - SESSION(IS.PREMARKET)         - Set trading session
// - ClosePosition(IS.BELL)        - Close position time
// - LongPosition or IsLongPosition or BUY - Open a long position (default)
// - ShortPosition or IsShortPosition or SELL - Open a short position
// - CloseLong                     - Close a long position
// - CloseShort                    - Close a short position
// - Enabled or IsEnabled or Enabled(Y) or IsEnabled(IS.True) - Enable strategy (default: true)
// - Enabled(N) or IsEnabled(IS.False) - Disable strategy
// - TimeInForce(DAY)              - Order time-in-force
// - OutsideRTH(true)              - Allow extended hours execution
// - AllOrNone(true)               - Require full fill or cancel
// - OrderType(LIMIT)              - Set order type
// - Repeat or IsRepeat or Repeat(Y) or IsRepeat(IS.True) - Strategy repeats after completion
// - Repeat(N) or IsRepeat(IS.False) - Strategy fires once (default)
//
// EXAMPLE:
// Ticker(NVDA).Session(IS.PREMARKET).ClosePosition(IS.PREMARKET.BELL).Qty(1).Entry(200).TakeProfit(201).StopLoss(190).TrailingStopLoss(10).Breakout().Pullback().AboveVwap.EmaBetween(9, 21).EmaAbove(200).MomentumAbove(0)
//
// REPEATING STRATEGY EXAMPLE:
// Ticker(ABC).Entry(5.00).TakeProfit(6.00).IsAboveVwap().IsDiPositive().IsRepeat()
//
// ============================================================================

using System.Text.RegularExpressions;
using IdiotProof.Shared.Enums;
using IdiotProof.Shared.Helpers;
using IdiotProof.Shared.Models;

namespace IdiotProof.Shared.Scripting;

/// <summary>
/// Universal parser for IdiotScript language.
/// Parses script strings into StrategyDefinition objects.
/// Uses period (.) as the universal delimiter.
/// </summary>
public static partial class IdiotScriptParser
{
    // ========================================================================
    // COMPILED REGEX PATTERNS
    // ========================================================================

    // Symbol patterns - supports TICKER(), SYM(), SYMBOL(), STOCK.TICKER(), STOCK.SYMBOL()
    [GeneratedRegex(@"^(?:STOCK\.)?(?:SYM|TICKER|SYMBOL)\(([A-Z0-9]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex SymbolPattern();

    // Strategy prefix pattern
    [GeneratedRegex(@"^STRATEGY$", RegexOptions.IgnoreCase)]
    private static partial Regex StrategyPrefixPattern();

    [GeneratedRegex(@"^(?:QTY|QUANTITY)\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityPattern();

    [GeneratedRegex(@"^(?:ENTRY|PRICE|ISPRICEABOVE)\((\$?[\d.]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"^(?:ISPRICEBELOW|PRICEBELOW)\((\$?[\d.]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex PriceBelowPattern();

    [GeneratedRegex(@"^(?:TP|TAKEPROFIT)\((\$?[\d.]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex TakeProfitPattern();

    [GeneratedRegex(@"^(?:SL|STOPLOSS)\((\$?[\d.]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex StopLossPattern();

    [GeneratedRegex(@"^(?:TSL|TRAILINGSTOPLOSS)\(([^)]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingStopLossPattern();

    [GeneratedRegex(@"^SESSION\(([^)]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex SessionPattern();

    [GeneratedRegex(@"^CLOSEPOSITION\(([^,)]+)(?:,\s*([\w.]+))?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex ClosePattern();

    [GeneratedRegex(@"^NAME\([""'](.+)[""']\)$", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^DESC\([""'](.+)[""']\)$", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionPattern();

    // Enabled patterns - supports: Enabled, IsEnabled, Enabled(), IsEnabled(), Enabled(Y), IsEnabled(IS.True), etc.
    [GeneratedRegex(@"^(?:IS)?ENABLED(?:\(([A-Za-z0-9_.]*)\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex EnabledPattern();

    // Condition patterns - Updated for new PascalCase syntax (no underscores)
    [GeneratedRegex(@"^(?:IS)?EMABETWEEN\((\d+)\s*,\s*(\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmaBetweenPattern();

    [GeneratedRegex(@"^(?:IS)?EMAABOVE\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmaAbovePattern();

    [GeneratedRegex(@"^(?:IS)?EMABELOW\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmaBelowPattern();

    [GeneratedRegex(@"^(?:IS)?RSIOVERSOLD\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RsiOversoldPattern();

    [GeneratedRegex(@"^(?:IS)?RSIOVERBOUGHT\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RsiOverboughtPattern();

    [GeneratedRegex(@"^(?:IS)?ADXABOVE\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex AdxPattern();

    [GeneratedRegex(@"^(?:IS)?MACDBULLISH(?:\(\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex MacdBullishPattern();

    [GeneratedRegex(@"^(?:IS)?MACDBEARISH(?:\(\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex MacdBearishPattern();

    [GeneratedRegex(@"^(?:IS)?DIPOSITIVE(?:\((\d+(?:\.\d+)?)\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DiPositivePattern();

    [GeneratedRegex(@"^(?:IS)?DINEGATIVE(?:\((\d+(?:\.\d+)?)\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DiNegativePattern();

    // Momentum patterns
    [GeneratedRegex(@"^(?:IS)?MOMENTUMABOVE\((-?\d+(?:\.\d+)?)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex MomentumAbovePattern();

    [GeneratedRegex(@"^(?:IS)?MOMENTUMBELOW\((-?\d+(?:\.\d+)?)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex MomentumBelowPattern();

    [GeneratedRegex(@"^(?:IS)?ROCABOVE\((-?\d+(?:\.\d+)?)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RocAbovePattern();

    [GeneratedRegex(@"^(?:IS)?ROCBELOW\((-?\d+(?:\.\d+)?)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RocBelowPattern();

    [GeneratedRegex(@"^BREAKOUT(?:\((\$?[\d.]*)\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex BreakoutPattern();

    [GeneratedRegex(@"^PULLBACK(?:\((\$?[\d.]*)\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PullbackPattern();

    // Order config patterns
    [GeneratedRegex(@"^TIMEINFORCE\(([A-Za-z]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeInForcePattern();

    [GeneratedRegex(@"^OUTSIDERTH\(([A-Za-z0-9_.]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex OutsideRthPattern();

    [GeneratedRegex(@"^ALLORNONE\(([A-Za-z0-9_.]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex AllOrNonePattern();

    [GeneratedRegex(@"^ORDERTYPE\(([A-Za-z]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex OrderTypePattern();

    // Execution behavior patterns - supports: Repeat, IsRepeat, Repeat(), IsRepeat(), Repeat(Y), IsRepeat(IS.True), etc.
    [GeneratedRegex(@"^(?:IS)?REPEAT(?:\(([A-Za-z0-9_.]*)\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatPattern();

    // Sanitization patterns
    [GeneratedRegex(@":\s*\(")]
    private static partial Regex ColonBeforeParenPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex ExtraWhitespacePattern();

    [GeneratedRegex(@"\(+")]
    private static partial Regex DoubleOpenParenPattern();

    [GeneratedRegex(@"\)+")]
    private static partial Regex DoubleCloseParenPattern();

    [GeneratedRegex(@",+")]
    private static partial Regex DoubleCommaPattern();

    [GeneratedRegex(@"\$+")]
    private static partial Regex DoubleDollarPattern();

    [GeneratedRegex(@"%+")]
    private static partial Regex DoublePercentPattern();

    // Whitespace inside parentheses patterns
    [GeneratedRegex(@"\(\s+")]
    private static partial Regex WhitespaceAfterOpenParenPattern();

    [GeneratedRegex(@"\s+\)")]
    private static partial Regex WhitespaceBeforeCloseParenPattern();

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    /// <summary>
    /// Parses an IdiotScript string into a StrategyDefinition.
    /// Uses period (.) as the universal delimiter.
    /// </summary>
    /// <param name="script">The script string to parse.</param>
    /// <param name="defaultSymbol">Default symbol if not specified in script.</param>
    /// <returns>A parsed StrategyDefinition.</returns>
    /// <exception cref="IdiotScriptException">Thrown when parsing fails.</exception>
    public static StrategyDefinition Parse(string script, string? defaultSymbol = null)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new IdiotScriptException("Script cannot be empty.");

        // Sanitize input
        script = Sanitize(script);

        var context = new ParseContext
        {
            Symbol = defaultSymbol
        };

        // Split by periods, but preserve IS. constants and method parameters
        var commands = SplitByDelimiter(script);

        foreach (var command in commands)
        {
            ParseCommand(command, context);
        }

        return BuildStrategy(context);
    }

    /// <summary>
    /// Splits the script by period delimiter while preserving IS. constants and parenthesized content.
    /// </summary>
    private static List<string> SplitByDelimiter(string script)
    {
        var commands = new List<string>();
        var current = new System.Text.StringBuilder();
        int parenDepth = 0;
        int i = 0;

        while (i < script.Length)
        {
            char c = script[i];

            if (c == '(')
            {
                parenDepth++;
                current.Append(c);
                i++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
                current.Append(c);
                i++;
            }
            else if (c == '.' && parenDepth == 0)
            {
                // Check if this is an IS. constant prefix (look ahead)
                if (i + 1 < script.Length && IsConstantPrefix(script, i))
                {
                    current.Append(c);
                    i++;
                }
                else
                {
                    // This is a delimiter
                    var cmd = current.ToString().Trim();
                    if (!string.IsNullOrEmpty(cmd))
                        commands.Add(cmd);
                    current.Clear();
                    i++;
                }
            }
            else
            {
                current.Append(c);
                i++;
            }
        }

        // Add the last command
        var lastCmd = current.ToString().Trim();
        if (!string.IsNullOrEmpty(lastCmd))
            commands.Add(lastCmd);

        return commands;
    }

    /// <summary>
    /// Checks if the period at position i is part of an IS. constant or STOCK. prefix.
    /// </summary>
    private static bool IsConstantPrefix(string script, int dotIndex)
    {
        // Check for IS. prefix (look back for "IS")
        if (dotIndex >= 2)
        {
            var prefix = script.Substring(dotIndex - 2, 2).ToUpperInvariant();
            if (prefix == "IS")
                return true;
        }

        // Check for STOCK. prefix (look back for "STOCK")
        if (dotIndex >= 5)
        {
            var prefix = script.Substring(dotIndex - 5, 5).ToUpperInvariant();
            if (prefix == "STOCK")
                return true;
        }

        // Check for nested IS. inside parentheses (e.g., SESSION(IS.PREMARKET))
        // Look ahead to see if what follows looks like an IS constant
        if (dotIndex + 1 < script.Length)
        {
            // Find the start of the potential constant by looking back
            int start = dotIndex - 1;
            while (start >= 0 && char.IsLetter(script[start]))
                start--;
            start++;

            if (start <= dotIndex)
            {
                var word = script.Substring(start, dotIndex - start).ToUpperInvariant();
                if (word == "IS")
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries to parse an IdiotScript string.
    /// </summary>
    public static bool TryParse(string script, out StrategyDefinition? strategy, out string? error, string? defaultSymbol = null)
    {
        try
        {
            strategy = Parse(script, defaultSymbol);
            error = null;
            return true;
        }
        catch (IdiotScriptException ex)
        {
            strategy = null;
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            strategy = null;
            error = $"Unexpected error: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Validates an IdiotScript without fully parsing it.
    /// </summary>
    public static (bool IsValid, List<string> Errors) Validate(string script)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(script))
        {
            errors.Add("Script cannot be empty.");
            return (false, errors);
        }

        try
        {
            Parse(script);
            return (true, errors);
        }
        catch (IdiotScriptException ex)
        {
            errors.Add(ex.Message);
            return (false, errors);
        }
    }

    // ========================================================================
    // SANITIZATION
    // ========================================================================

    /// <summary>
    /// Sanitizes the input script to handle common typos and formatting issues.
    /// Also converts commands to PascalCase for consistent output.
    /// </summary>
    private static string Sanitize(string script)
    {
        // Remove colons before parentheses (e.g., TP:(201) -> TP(201))
        script = ColonBeforeParenPattern().Replace(script, "(");

        // Consolidate doubled characters
        script = DoubleOpenParenPattern().Replace(script, "(");
        script = DoubleCloseParenPattern().Replace(script, ")");
        script = DoubleCommaPattern().Replace(script, ",");
        script = DoubleDollarPattern().Replace(script, "$");
        script = DoublePercentPattern().Replace(script, "%");

        // Remove whitespace inside parentheses (e.g., TICKER( AAPL ) -> TICKER(AAPL))
        script = WhitespaceAfterOpenParenPattern().Replace(script, "(");
        script = WhitespaceBeforeCloseParenPattern().Replace(script, ")");

        // Remove extra whitespace
        script = ExtraWhitespacePattern().Replace(script, " ");

        // Convert to PascalCase for consistency
        script = ConvertToPascalCase(script);

        return script.Trim();
    }

    /// <summary>
    /// Converts script commands to PascalCase format while preserving
    /// content inside parentheses and IS. constants.
    /// </summary>
    private static string ConvertToPascalCase(string script)
    {
        var result = new System.Text.StringBuilder(script.Length);
        int parenDepth = 0;
        int i = 0;

        while (i < script.Length)
        {
            char c = script[i];

            if (c == '(')
            {
                parenDepth++;
                result.Append(c);
                i++;
            }
            else if (c == ')')
            {
                parenDepth = Math.Max(0, parenDepth - 1);
                result.Append(c);
                i++;
            }
            else if (parenDepth == 0 && char.IsLetter(c))
            {
                // Extract the command word
                int start = i;
                while (i < script.Length && (char.IsLetterOrDigit(script[i]) || script[i] == '_'))
                    i++;

                var word = script.Substring(start, i - start);

                // Check for IS. constant prefix (don't convert, keep as-is)
                if (word.Equals("IS", StringComparison.OrdinalIgnoreCase) &&
                    i < script.Length && script[i] == '.')
                {
                    result.Append("IS");
                }
                else if (word.Equals("STOCK", StringComparison.OrdinalIgnoreCase) &&
                    i < script.Length && script[i] == '.')
                {
                    result.Append("Stock");
                }
                else
                {
                    // Convert to PascalCase (remove underscores, capitalize first letter of each word)
                    result.Append(ToPascalCase(word));
                }
            }
            else
            {
                result.Append(c);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts a word to PascalCase, removing underscores.
    /// 
    /// </summary>
    private static string ToPascalCase(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Command name mappings
        var upperWord = word.ToUpperInvariant();
        var mapped = upperWord switch
        {
            // VWAP/EMA/RSI/ADX mappings
            "ABOVE_VWAP" => "AboveVwap",
            "BELOW_VWAP" => "BelowVwap",
            "ABOVE_EMA" => "EmaAbove",
            "BELOW_EMA" => "EmaBelow",
            "BETWEEN_EMA" => "EmaBetween",
            "RSI_OVERSOLD" => "RsiOversold",
            "RSI_OVERBOUGHT" => "RsiOverbought",
            "ADX_ABOVE" => "AdxAbove",
            // OPEN -> Entry mapping
            "OPEN" => "Entry",
            // CLOSE -> ClosePosition mapping
            "CLOSE" => "ClosePosition",
            _ => null
        };

        if (mapped != null)
            return mapped;

        // Handle underscore-separated words (e.g., SOME_OTHER -> SomeOther)
        if (word.Contains('_'))
        {
            var parts = word.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length > 0)
                {
                    sb.Append(char.ToUpperInvariant(part[0]));
                    if (part.Length > 1)
                        sb.Append(part[1..].ToLowerInvariant());
                }
            }
            return sb.ToString();
        }

        // Simple PascalCase: first letter upper, rest lower
        if (word.Length == 1)
            return word.ToUpperInvariant();

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    // ========================================================================
    // COMMAND PARSING
    // ========================================================================

    private static void ParseCommand(string command, ParseContext context)
    {
        var upperCommand = command.ToUpperInvariant();

        // Skip STRATEGY prefix (just a marker)
        if (StrategyPrefixPattern().IsMatch(command)) return;

        // Symbol commands (including STOCK. prefix)
        if (TryParseSymbol(command, context)) return;

        // Quantity
        if (TryParseQuantity(command, context)) return;

        // Entry/Open price
        if (TryParseEntry(command, context)) return;

        // Take Profit
        if (TryParseTakeProfit(command, context)) return;

        // Stop Loss
        if (TryParseStopLoss(command, context)) return;

        // Trailing Stop Loss
        if (TryParseTrailingStopLoss(command, context)) return;

        // Session
        if (TryParseSession(command, context)) return;

        // Close position
        if (TryParseClose(command, context)) return;

        // Order direction
        if (TryParseDirection(upperCommand, context)) return;

        // Close long/short
        if (TryParseCloseLong(upperCommand, context)) return;
        if (TryParseCloseShort(upperCommand, context)) return;

        // Name
        if (TryParseName(command, context)) return;

        // Description
        if (TryParseDescription(command, context)) return;

        // Enabled
        if (TryParseEnabled(command, context)) return;

        // Order config
        if (TryParseTimeInForce(command, context)) return;
        if (TryParseOutsideRth(command, context)) return;
        if (TryParseAllOrNone(command, context)) return;
        if (TryParseOrderType(command, context)) return;

        // Execution behavior
        if (TryParseRepeat(command, context)) return;

        // Condition keywords (add to ordered conditions)
        if (TryParseCondition(command, upperCommand, context)) return;

        throw new IdiotScriptException($"Unknown command: {command}");
    }

    /// <summary>
    /// Parses a single condition command.
    /// </summary>
    private static bool TryParseCondition(string command, string upper, ParseContext context)
    {
        var condition = ParseSingleCondition(command);
        if (condition != null)
        {
            context.OrderedConditions.Add(condition);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a single condition from the command.
    /// </summary>
    private static OrderedCondition? ParseSingleCondition(string condition)
    {
        var upper = condition.ToUpperInvariant();

        // BREAKOUT or BREAKOUT() or BREAKOUT(price)
        var breakoutMatch = BreakoutPattern().Match(condition);
        if (breakoutMatch.Success)
        {
            double? price = null;
            if (breakoutMatch.Groups[1].Success && !string.IsNullOrEmpty(breakoutMatch.Groups[1].Value))
            {
                var priceStr = breakoutMatch.Groups[1].Value.Replace("$", "");
                if (double.TryParse(priceStr, out var p)) price = p;
            }
            return new OrderedCondition(ConditionType.Breakout, price);
        }

        // PULLBACK or PULLBACK() or PULLBACK(price)
        var pullbackMatch = PullbackPattern().Match(condition);
        if (pullbackMatch.Success)
        {
            double? price = null;
            if (pullbackMatch.Groups[1].Success && !string.IsNullOrEmpty(pullbackMatch.Groups[1].Value))
            {
                var priceStr = pullbackMatch.Groups[1].Value.Replace("$", "");
                if (double.TryParse(priceStr, out var p)) price = p;
            }
            return new OrderedCondition(ConditionType.Pullback, price);
        }

        // ABOVEVWAP, VWAP, ISABOVEVWAP, ABOVEVWAP(), ISABOVEVWAP() (case insensitive due to PascalCase conversion)
        if (upper == "ABOVEVWAP" || upper == "VWAP" || upper == "ISABOVEVWAP" ||
            upper == "ABOVEVWAP()" || upper == "ISABOVEVWAP()")
            return new OrderedCondition(ConditionType.AboveVwap);

        // BELOWVWAP, ISBELOWVWAP, BELOWVWAP(), ISBELOWVWAP()
        if (upper == "BELOWVWAP" || upper == "ISBELOWVWAP" ||
            upper == "BELOWVWAP()" || upper == "ISBELOWVWAP()")
            return new OrderedCondition(ConditionType.BelowVwap);

        // EmaBetween(lower, upper) or IsEmaBetween(lower, upper)
        var emaBetweenMatch = EmaBetweenPattern().Match(condition);
        if (emaBetweenMatch.Success)
        {
            if (int.TryParse(emaBetweenMatch.Groups[1].Value, out var lower) &&
                int.TryParse(emaBetweenMatch.Groups[2].Value, out var upper2))
                return new OrderedCondition(ConditionType.EmaBetween, period: lower, period2: upper2);
        }

        // EmaAbove(period) or IsEmaAbove(period)
        var emaAboveMatch = EmaAbovePattern().Match(condition);
        if (emaAboveMatch.Success)
        {
            if (int.TryParse(emaAboveMatch.Groups[1].Value, out var period))
                return new OrderedCondition(ConditionType.EmaAbove, period: period);
        }

        // EmaBelow(period) or IsEmaBelow(period)
        var emaBelowMatch = EmaBelowPattern().Match(condition);
        if (emaBelowMatch.Success)
        {
            if (int.TryParse(emaBelowMatch.Groups[1].Value, out var period))
                return new OrderedCondition(ConditionType.EmaBelow, period: period);
        }

        // RsiOversold(value) or IsRsiOversold(value)
        var rsiOversoldMatch = RsiOversoldPattern().Match(condition);
        if (rsiOversoldMatch.Success)
        {
            if (double.TryParse(rsiOversoldMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.RsiOversold, value);
        }

        // RsiOverbought(value) or IsRsiOverbought(value)
        var rsiOverboughtMatch = RsiOverboughtPattern().Match(condition);
        if (rsiOverboughtMatch.Success)
        {
            if (double.TryParse(rsiOverboughtMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.RsiOverbought, value);
        }

        // AdxAbove(value) or IsAdxAbove(value)
        var adxMatch = AdxPattern().Match(condition);
        if (adxMatch.Success)
        {
            if (double.TryParse(adxMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.AdxAbove, value);
        }

        // Entry/Price/IsPriceAbove condition
        var entryMatch = EntryPattern().Match(condition);
        if (entryMatch.Success)
        {
            var priceStr = entryMatch.Groups[1].Value.Replace("$", "");
            if (double.TryParse(priceStr, out var price))
                return new OrderedCondition(ConditionType.PriceAbove, price);
        }

        // IsPriceBelow/PriceBelow condition
        var priceBelowMatch = PriceBelowPattern().Match(condition);
        if (priceBelowMatch.Success)
        {
            var priceStr = priceBelowMatch.Groups[1].Value.Replace("$", "");
            if (double.TryParse(priceStr, out var price))
                return new OrderedCondition(ConditionType.PriceBelow, price);
        }

        // MACD Bullish or IsMacdBullish
        if (MacdBullishPattern().IsMatch(condition))
            return new OrderedCondition(ConditionType.MacdBullish);

        // MACD Bearish or IsMacdBearish
        if (MacdBearishPattern().IsMatch(condition))
            return new OrderedCondition(ConditionType.MacdBearish);

        // DI Positive or IsDiPositive (optional threshold)
        var diPositiveMatch = DiPositivePattern().Match(condition);
        if (diPositiveMatch.Success)
        {
            double? threshold = null;
            if (diPositiveMatch.Groups[1].Success && !string.IsNullOrEmpty(diPositiveMatch.Groups[1].Value))
            {
                if (double.TryParse(diPositiveMatch.Groups[1].Value, out var t))
                    threshold = t;
            }
            return new OrderedCondition(ConditionType.DiPositive, threshold ?? 25); // Default threshold is 25
        }

        // DI Negative or IsDiNegative (optional threshold)
        var diNegativeMatch = DiNegativePattern().Match(condition);
        if (diNegativeMatch.Success)
        {
            double? threshold = null;
            if (diNegativeMatch.Groups[1].Success && !string.IsNullOrEmpty(diNegativeMatch.Groups[1].Value))
            {
                if (double.TryParse(diNegativeMatch.Groups[1].Value, out var t))
                    threshold = t;
            }
            return new OrderedCondition(ConditionType.DiNegative, threshold ?? 25); // Default threshold is 25
        }

        // Momentum Above or IsMomentumAbove
        var momentumAboveMatch = MomentumAbovePattern().Match(condition);
        if (momentumAboveMatch.Success)
        {
            if (double.TryParse(momentumAboveMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.MomentumAbove, value);
        }

        // Momentum Below or IsMomentumBelow
        var momentumBelowMatch = MomentumBelowPattern().Match(condition);
        if (momentumBelowMatch.Success)
        {
            if (double.TryParse(momentumBelowMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.MomentumBelow, value);
        }

        // ROC Above or IsRocAbove
        var rocAboveMatch = RocAbovePattern().Match(condition);
        if (rocAboveMatch.Success)
        {
            if (double.TryParse(rocAboveMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.RocAbove, value);
        }

        // ROC Below or IsRocBelow
        var rocBelowMatch = RocBelowPattern().Match(condition);
        if (rocBelowMatch.Success)
        {
            if (double.TryParse(rocBelowMatch.Groups[1].Value, out var value))
                return new OrderedCondition(ConditionType.RocBelow, value);
        }

        return null;
    }

    private static bool TryParseSymbol(string command, ParseContext context)
    {
        var match = SymbolPattern().Match(command);
        if (!match.Success) return false;

        context.Symbol = match.Groups[1].Value.ToUpperInvariant();
        return true;
    }

    private static bool TryParseQuantity(string command, ParseContext context)
    {
        var match = QuantityPattern().Match(command);
        if (!match.Success) return false;

        if (int.TryParse(match.Groups[1].Value, out var qty))
        {
            context.Quantity = qty;
            return true;
        }

        throw new IdiotScriptException($"Invalid quantity: {match.Groups[1].Value}");
    }

    private static bool TryParseEntry(string command, ParseContext context)
    {
        var match = EntryPattern().Match(command);
        if (!match.Success) return false;

        var priceStr = match.Groups[1].Value.Replace("$", "");
        if (double.TryParse(priceStr, out var price))
        {
            context.EntryPrice = price;
            return true;
        }

        throw new IdiotScriptException($"Invalid entry price: {match.Groups[1].Value}");
    }

    private static bool TryParseTakeProfit(string command, ParseContext context)
    {
        var match = TakeProfitPattern().Match(command);
        if (!match.Success) return false;

        var priceStr = match.Groups[1].Value.Replace("$", "");
        if (double.TryParse(priceStr, out var price))
        {
            context.TakeProfitPrice = price;
            return true;
        }

        throw new IdiotScriptException($"Invalid take profit price: {match.Groups[1].Value}");
    }

    private static bool TryParseStopLoss(string command, ParseContext context)
    {
        var match = StopLossPattern().Match(command);
        if (!match.Success) return false;

        var priceStr = match.Groups[1].Value.Replace("$", "");
        if (double.TryParse(priceStr, out var price))
        {
            context.StopLossPrice = price;
            return true;
        }

        throw new IdiotScriptException($"Invalid stop loss price: {match.Groups[1].Value}");
    }

    private static bool TryParseTrailingStopLoss(string command, ParseContext context)
    {
        var match = TrailingStopLossPattern().Match(command);
        if (!match.Success) return false;

        var valueStr = match.Groups[1].Value.Trim();

        // Check if it's a constant (IS. prefix)
        if (IdiotScriptConstants.IsConstant(valueStr))
        {
            var resolved = IdiotScriptConstants.ResolveDouble(valueStr);
            if (resolved.HasValue)
            {
                context.TrailingStopLossPercent = resolved.Value;
                return true;
            }
            throw new IdiotScriptException($"Unknown constant: {valueStr}");
        }

        // Parse as number/percentage
        valueStr = valueStr.Replace("%", "");
        if (double.TryParse(valueStr, out var value))
        {
            // If value > 1, treat as percentage (e.g., 15 -> 0.15)
            context.TrailingStopLossPercent = value > 1 ? value / 100.0 : value;
            return true;
        }

        throw new IdiotScriptException($"Invalid trailing stop loss: {match.Groups[1].Value}");
    }

    private static bool TryParseSession(string command, ParseContext context)
    {
        var match = SessionPattern().Match(command);
        if (!match.Success) return false;

        var sessionStr = match.Groups[1].Value.Trim();

        // Check if it's a constant (IS. prefix)
        if (IdiotScriptConstants.IsConstant(sessionStr))
        {
            var resolved = IdiotScriptConstants.ResolveConstant(sessionStr);
            if (resolved != null && Enum.TryParse<TradingSession>(resolved, ignoreCase: true, out var sessionConst))
            {
                context.Session = sessionConst;
                return true;
            }
            throw new IdiotScriptException($"Unknown session constant: {sessionStr}");
        }

        // Try direct enum parse
        if (Enum.TryParse<TradingSession>(sessionStr, ignoreCase: true, out var session))
        {
            context.Session = session;
            return true;
        }

        // Try common aliases
        context.Session = sessionStr.ToUpperInvariant() switch
        {
            "PM" or "PREMARKET" or "PRE" => TradingSession.PreMarket,
            "AH" or "AFTERHOURS" or "AFTER" => TradingSession.AfterHours,
            "REG" or "REGULAR" => TradingSession.RTH,
            "EXT" or "EXTENDED" => TradingSession.Extended,
            "EARLY" or "PMENDEARLY" => TradingSession.PreMarketEndEarly,
            _ => throw new IdiotScriptException($"Unknown session: {sessionStr}")
        };

        return true;
    }

    private static bool TryParseClose(string command, ParseContext context)
    {
        var match = ClosePattern().Match(command);
        if (!match.Success) return false;

        var timeArg = match.Groups[1].Value.Trim();

        // Parse the optional profitable-only flag using centralized boolean resolver
        var profitOnly = match.Groups.Count > 2 && !string.IsNullOrEmpty(match.Groups[2].Value) &&
            (IdiotScriptConstants.ResolveBoolean(match.Groups[2].Value.Trim()) ?? false);

        // Check if it's a constant (IS. prefix)
        if (IdiotScriptConstants.IsConstant(timeArg))
        {
            var resolved = IdiotScriptConstants.ResolveTime(timeArg);
            if (resolved.HasValue)
            {
                context.ClosePositionTime = resolved.Value;
                context.CloseOnlyIfProfitable = profitOnly;
                return true;
            }
            throw new IdiotScriptException($"Unknown time constant: {timeArg}");
        }

        // Try to parse as time (HH:mm)
        if (TimeOnly.TryParse(timeArg, out var time))
        {
            context.ClosePositionTime = time;
            context.CloseOnlyIfProfitable = profitOnly;
            return true;
        }

        // Try common aliases
        context.ClosePositionTime = timeArg.ToUpperInvariant() switch
        {
            "ENDING" or "END" or "BELL" => MarketTime.PreMarket.RightBeforeBell,
            "OPEN" => MarketTime.RTH.Start,
            "CLOSE" or "EOD" => MarketTime.RTH.End,
            _ => throw new IdiotScriptException($"Unknown close time: {timeArg}")
        };
        context.CloseOnlyIfProfitable = profitOnly;

        return true;
    }

    private static bool TryParseDirection(string upper, ParseContext context)
    {
        // LongPosition, IsLongPosition, or BUY
        if (upper is "LONGPOSITION" or "ISLONGPOSITION" or "BUY" or "IS.BUY")
        {
            context.IsBuy = true;
            return true;
        }
        // ShortPosition, IsShortPosition, or SELL
        if (upper is "SHORTPOSITION" or "ISSHORTPOSITION" or "SELL" or "IS.SELL")
        {
            context.IsBuy = false;
            return true;
        }
        return false;
    }

    private static bool TryParseName(string command, ParseContext context)
    {
        var match = NamePattern().Match(command);
        if (!match.Success) return false;

        context.Name = match.Groups[1].Value;
        return true;
    }

    private static bool TryParseDescription(string command, ParseContext context)
    {
        var match = DescriptionPattern().Match(command);
        if (!match.Success) return false;

        context.Description = match.Groups[1].Value;
        return true;
    }

    private static bool TryParseEnabled(string command, ParseContext context)
    {
        var match = EnabledPattern().Match(command);
        if (!match.Success) return false;

        var value = match.Groups[1].Value;

        // If no value provided (e.g., "Enabled", "IsEnabled", "Enabled()", "IsEnabled()"), default to true
        if (string.IsNullOrEmpty(value))
        {
            context.Enabled = true;
            return true;
        }

        // Use the centralized boolean resolver
        var resolved = IdiotScriptConstants.ResolveBoolean(value);
        if (resolved.HasValue)
        {
            context.Enabled = resolved.Value;
            return true;
        }

        throw new IdiotScriptException($"Invalid boolean value: {value}. Valid values: Y, YES, TRUE, N, NO, FALSE, IS.TRUE, IS.FALSE");
    }

    private static bool TryParseCloseLong(string upper, ParseContext context)
    {
        if (upper == "CLOSELONG")
        {
            context.CloseOrderType = CloseOrderType.CloseLong;
            return true;
        }
        return false;
    }

    private static bool TryParseCloseShort(string upper, ParseContext context)
    {
        if (upper == "CLOSESHORT")
        {
            context.CloseOrderType = CloseOrderType.CloseShort;
            return true;
        }
        return false;
    }

    private static bool TryParseTimeInForce(string command, ParseContext context)
    {
        var match = TimeInForcePattern().Match(command);
        if (!match.Success) return false;

        var value = match.Groups[1].Value;

        // Try direct enum parse
        if (Enum.TryParse<TimeInForce>(value, ignoreCase: true, out var tif))
        {
            context.TimeInForce = tif;
            return true;
        }

        // Common aliases
        context.TimeInForce = value.ToUpperInvariant() switch
        {
            "DAY" => TimeInForce.Day,
            "GTC" => TimeInForce.GoodTillCancel,
            "IOC" => TimeInForce.ImmediateOrCancel,
            "FOK" => TimeInForce.FillOrKill,
            _ => throw new IdiotScriptException($"Unknown time in force: {value}. Valid values: DAY, GTC, IOC, FOK")
        };

        return true;
    }

    private static bool TryParseOutsideRth(string command, ParseContext context)
    {
        var match = OutsideRthPattern().Match(command);
        if (!match.Success) return false;

        var value = match.Groups[1].Value;
        var resolved = IdiotScriptConstants.ResolveBoolean(value);
        if (resolved.HasValue)
        {
            context.OutsideRth = resolved.Value;
            return true;
        }

        throw new IdiotScriptException($"Invalid boolean value for OutsideRTH: {value}");
    }

    private static bool TryParseAllOrNone(string command, ParseContext context)
    {
        var match = AllOrNonePattern().Match(command);
        if (!match.Success) return false;

        var value = match.Groups[1].Value;
        var resolved = IdiotScriptConstants.ResolveBoolean(value);
        if (resolved.HasValue)
        {
            context.AllOrNone = resolved.Value;
            return true;
        }

        throw new IdiotScriptException($"Invalid boolean value for AllOrNone: {value}");
    }

    private static bool TryParseOrderType(string command, ParseContext context)
    {
        var match = OrderTypePattern().Match(command);
        if (!match.Success) return false;

        var value = match.Groups[1].Value;

        // Try direct enum parse
        if (Enum.TryParse<OrderType>(value, ignoreCase: true, out var orderType))
        {
            context.OrderType = orderType;
            return true;
        }

        // Common aliases
        context.OrderType = value.ToUpperInvariant() switch
        {
            "MKT" => OrderType.Market,
            "LMT" => OrderType.Limit,
            _ => throw new IdiotScriptException($"Unknown order type: {value}. Valid values: MARKET, LIMIT, MKT, LMT")
        };

        return true;
    }

    private static bool TryParseRepeat(string command, ParseContext context)
    {
        var match = RepeatPattern().Match(command);
        if (!match.Success) return false;

        var value = match.Groups[1].Value;

        // If no value provided (e.g., "Repeat", "IsRepeat", "Repeat()", "IsRepeat()"), default to true
        if (string.IsNullOrEmpty(value))
        {
            context.RepeatEnabled = true;
            return true;
        }

        // Use the centralized boolean resolver
        var resolved = IdiotScriptConstants.ResolveBoolean(value);
        if (resolved.HasValue)
        {
            context.RepeatEnabled = resolved.Value;
            return true;
        }

        throw new IdiotScriptException($"Invalid boolean value for Repeat: {value}. Valid values: Y, YES, TRUE, N, NO, FALSE, IS.TRUE, IS.FALSE");
    }

    // ========================================================================
    // STRATEGY BUILDING
    // ========================================================================

    private static StrategyDefinition BuildStrategy(ParseContext context)
    {
        if (string.IsNullOrEmpty(context.Symbol))
            throw new IdiotScriptException("Symbol is required. Use SYM(AAPL) or TICKER(AAPL).");

        var strategy = new StrategyDefinition
        {
            Name = context.Name ?? $"{context.Symbol} Strategy",
            Symbol = context.Symbol,
            Description = context.Description,
            Enabled = context.Enabled,
            RepeatEnabled = context.RepeatEnabled,
            Segments = []
        };

        int segmentOrder = 1;

        // Add ticker segment
        strategy.Segments.Add(new StrategySegment
        {
            Type = SegmentType.Ticker,
            Category = SegmentCategory.Start,
            DisplayName = "Ticker",
            Order = segmentOrder++,
            Parameters = [new SegmentParameter
            {
                Name = "Symbol",
                Label = "Symbol",
                Type = ParameterType.String,
                Value = context.Symbol,
                IsRequired = true
            }]
        });

        // Add session segment
        if (context.Session.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.SessionDuration,
                Category = SegmentCategory.Session,
                DisplayName = "Session Duration",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Session",
                    Label = "Session",
                    Type = ParameterType.Enum,
                    EnumTypeName = nameof(TradingSession),
                    Value = context.Session.ToString(),
                    IsRequired = true
                }]
            });
        }

        // Add conditions in sequence
        foreach (var condition in context.OrderedConditions)
        {
            var segment = CreateConditionSegment(condition, ref segmentOrder);
            if (segment != null)
                strategy.Segments.Add(segment);
        }

        // If no ordered conditions but entry price specified, add it
        if (context.OrderedConditions.Count == 0 && context.EntryPrice.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.IsPriceAbove,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Price Above",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Level",
                    Label = "Level",
                    Type = ParameterType.Price,
                    Value = context.EntryPrice.Value,
                    IsRequired = true
                }]
            });
        }

        // Add order segment (LongPosition, ShortPosition, CloseLong, or CloseShort)
        var orderSegmentType = context.CloseOrderType switch
        {
            CloseOrderType.CloseLong => SegmentType.CloseLong,
            CloseOrderType.CloseShort => SegmentType.CloseShort,
            _ => context.IsBuy ? SegmentType.Buy : SegmentType.Sell
        };

        var orderDisplayName = context.CloseOrderType switch
        {
            CloseOrderType.CloseLong => "Close Long",
            CloseOrderType.CloseShort => "Close Short",
            _ => context.IsBuy ? "Long Position" : "Short Position"
        };

        var orderParams = new List<SegmentParameter>
        {
            new()
            {
                Name = "Quantity",
                Label = "Quantity",
                Type = ParameterType.Integer,
                Value = context.Quantity,
                IsRequired = true
            },
            new()
            {
                Name = "PriceType",
                Label = "Price Type",
                Type = ParameterType.Enum,
                EnumTypeName = nameof(Price),
                Value = Price.Current.ToString(),
                IsRequired = true
            }
        };

        if (context.EntryPrice.HasValue)
        {
            orderParams.Add(new SegmentParameter
            {
                Name = "LimitPrice",
                Label = "Limit Price",
                Type = ParameterType.Price,
                Value = context.EntryPrice.Value,
                IsRequired = false
            });
        }

        strategy.Segments.Add(new StrategySegment
        {
            Type = orderSegmentType,
            Category = SegmentCategory.Order,
            DisplayName = orderDisplayName,
            Order = segmentOrder++,
            Parameters = orderParams
        });

        // Add take profit
        if (context.TakeProfitPrice.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.TakeProfit,
                Category = SegmentCategory.RiskManagement,
                DisplayName = "Take Profit",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Price",
                    Label = "Price",
                    Type = ParameterType.Price,
                    Value = context.TakeProfitPrice.Value,
                    IsRequired = true
                }]
            });
        }

        // Add stop loss
        if (context.StopLossPrice.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.StopLoss,
                Category = SegmentCategory.RiskManagement,
                DisplayName = "Stop Loss",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Price",
                    Label = "Price",
                    Type = ParameterType.Price,
                    Value = context.StopLossPrice.Value,
                    IsRequired = true
                }]
            });
        }

        // Add trailing stop loss
        if (context.TrailingStopLossPercent.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.TrailingStopLoss,
                Category = SegmentCategory.RiskManagement,
                DisplayName = "Trailing Stop Loss",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Percentage",
                    Label = "Percentage",
                    Type = ParameterType.Percentage,
                    Value = context.TrailingStopLossPercent.Value,
                    IsRequired = true
                }]
            });
        }

        // Add close position (LAST ONE WINS - overrides session end time)
        if (context.ClosePositionTime.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.ClosePosition,
                Category = SegmentCategory.PositionManagement,
                DisplayName = "Close Position",
                Order = segmentOrder++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Time",
                        Label = "Time",
                        Type = ParameterType.Time,
                        Value = context.ClosePositionTime.Value,
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "OnlyIfProfitable",
                        Label = "Only If Profitable",
                        Type = ParameterType.Boolean,
                        Value = context.CloseOnlyIfProfitable,
                        IsRequired = false
                    }
                ]
            });
        }

        // Add order config segments
        if (context.TimeInForce.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.TimeInForce,
                Category = SegmentCategory.OrderConfig,
                DisplayName = "Time In Force",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Value",
                    Label = "Time In Force",
                    Type = ParameterType.Enum,
                    EnumTypeName = nameof(TimeInForce),
                    Value = context.TimeInForce.Value.ToString(),
                    IsRequired = true
                }]
            });
        }

        if (context.OutsideRth.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.OutsideRTH,
                Category = SegmentCategory.OrderConfig,
                DisplayName = "Outside RTH",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Value",
                    Label = "Allow Outside RTH",
                    Type = ParameterType.Boolean,
                    Value = context.OutsideRth.Value,
                    IsRequired = true
                }]
            });
        }

        if (context.AllOrNone.HasValue)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.AllOrNone,
                Category = SegmentCategory.OrderConfig,
                DisplayName = "All Or None",
                Order = segmentOrder++,
                Parameters = [new SegmentParameter
                {
                    Name = "Value",
                    Label = "All Or None",
                    Type = ParameterType.Boolean,
                    Value = context.AllOrNone.Value,
                    IsRequired = true
                }]
            });
        }

        // Always add order type segment (defaults to LIMIT for safe premarket/after-hours execution)
        strategy.Segments.Add(new StrategySegment
        {
            Type = SegmentType.OrderType,
            Category = SegmentCategory.OrderConfig,
            DisplayName = "Order Type",
            Order = segmentOrder++,
            Parameters = [new SegmentParameter
            {
                Name = "Value",
                Label = "Order Type",
                Type = ParameterType.Enum,
                EnumTypeName = nameof(OrderType),
                Value = context.OrderType.ToString(),
                IsRequired = true
            }]
        });

        // Add repeat segment if enabled
        if (context.RepeatEnabled)
        {
            strategy.Segments.Add(new StrategySegment
            {
                Type = SegmentType.Repeat,
                Category = SegmentCategory.Execution,
                DisplayName = "Repeat",
                Order = segmentOrder++,
                Parameters = []
            });
        }

        return strategy;
    }

    private static StrategySegment? CreateConditionSegment(OrderedCondition condition, ref int order)
    {
        return condition.Type switch
        {
            ConditionType.Breakout when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.Breakout,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Breakout",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "Level",
                    Label = "Level",
                    Type = ParameterType.Price,
                    Value = condition.Value.Value,
                    IsRequired = true
                }]
            },
            ConditionType.Breakout => new StrategySegment
            {
                Type = SegmentType.Breakout,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Breakout",
                Order = order++,
                Parameters = []
            },
            ConditionType.Pullback when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.Pullback,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Pullback",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "Level",
                    Label = "Level",
                    Type = ParameterType.Price,
                    Value = condition.Value.Value,
                    IsRequired = true
                }]
            },
            ConditionType.Pullback => new StrategySegment
            {
                Type = SegmentType.Pullback,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Pullback",
                Order = order++,
                Parameters = []
            },
            ConditionType.PriceAbove when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsPriceAbove,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Price Above",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "Level",
                    Label = "Level",
                    Type = ParameterType.Price,
                    Value = condition.Value.Value,
                    IsRequired = true
                }]
            },
            ConditionType.PriceBelow when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsPriceBelow,
                Category = SegmentCategory.PriceCondition,
                DisplayName = "Price Below",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "Level",
                    Label = "Level",
                    Type = ParameterType.Price,
                    Value = condition.Value.Value,
                    IsRequired = true
                }]
            },
            ConditionType.AboveVwap => new StrategySegment
            {
                Type = SegmentType.IsAboveVwap,
                Category = SegmentCategory.VwapCondition,
                DisplayName = "Above VWAP",
                Order = order++,
                Parameters = []
            },
            ConditionType.BelowVwap => new StrategySegment
            {
                Type = SegmentType.IsBelowVwap,
                Category = SegmentCategory.VwapCondition,
                DisplayName = "Below VWAP",
                Order = order++,
                Parameters = []
            },
            ConditionType.EmaAbove when condition.Period.HasValue => new StrategySegment
            {
                Type = SegmentType.IsEmaAbove,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = $"Above EMA {condition.Period}",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = condition.Period.Value,
                    IsRequired = true
                }]
            },
            ConditionType.EmaBelow when condition.Period.HasValue => new StrategySegment
            {
                Type = SegmentType.IsEmaBelow,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = $"Below EMA {condition.Period}",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "Period",
                    Label = "Period",
                    Type = ParameterType.Integer,
                    Value = condition.Period.Value,
                    IsRequired = true
                }]
            },
            ConditionType.EmaBetween when condition.Period.HasValue && condition.Period2.HasValue => new StrategySegment
            {
                Type = SegmentType.IsEmaBetween,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = $"Between EMA {condition.Period} and {condition.Period2}",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "LowerPeriod",
                        Label = "Lower Period",
                        Type = ParameterType.Integer,
                        Value = condition.Period.Value,
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "UpperPeriod",
                        Label = "Upper Period",
                        Type = ParameterType.Integer,
                        Value = condition.Period2.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.RsiOversold when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsRsi,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "RSI Oversold",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.Enum,
                        EnumTypeName = nameof(RsiCondition),
                        Value = RsiCondition.Below.ToString(),
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Value",
                        Label = "Value",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.RsiOverbought when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsRsi,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "RSI Overbought",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.Enum,
                        EnumTypeName = nameof(RsiCondition),
                        Value = RsiCondition.Above.ToString(),
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Value",
                        Label = "Value",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.AdxAbove when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsAdx,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "ADX Above",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.Enum,
                        EnumTypeName = nameof(AdxCondition),
                        Value = AdxCondition.Above.ToString(),
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Value",
                        Label = "Value",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.MacdBullish => new StrategySegment
            {
                Type = SegmentType.IsMacd,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "MACD Bullish",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "State",
                    Label = "State",
                    Type = ParameterType.Enum,
                    EnumTypeName = nameof(MacdState),
                    Value = MacdState.Bullish.ToString(),
                    IsRequired = true
                }]
            },
            ConditionType.MacdBearish => new StrategySegment
            {
                Type = SegmentType.IsMacd,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "MACD Bearish",
                Order = order++,
                Parameters = [new SegmentParameter
                {
                    Name = "State",
                    Label = "State",
                    Type = ParameterType.Enum,
                    EnumTypeName = nameof(MacdState),
                    Value = MacdState.Bearish.ToString(),
                    IsRequired = true
                }]
            },
            ConditionType.DiPositive when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsDI,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "+DI Positive",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Direction",
                        Label = "Direction",
                        Type = ParameterType.Enum,
                        EnumTypeName = nameof(DiDirection),
                        Value = DiDirection.Positive.ToString(),
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Threshold",
                        Label = "Threshold",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.DiNegative when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsDI,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "-DI Negative",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Direction",
                        Label = "Direction",
                        Type = ParameterType.Enum,
                        EnumTypeName = nameof(DiDirection),
                        Value = DiDirection.Negative.ToString(),
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Threshold",
                        Label = "Threshold",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.MomentumAbove when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsMomentum,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "Momentum Above",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.String,
                        Value = "Above",
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Threshold",
                        Label = "Threshold",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.MomentumBelow when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsMomentum,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "Momentum Below",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.String,
                        Value = "Below",
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Threshold",
                        Label = "Threshold",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.RocAbove when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsRoc,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "ROC Above",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.String,
                        Value = "Above",
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Threshold",
                        Label = "Threshold",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            ConditionType.RocBelow when condition.Value.HasValue => new StrategySegment
            {
                Type = SegmentType.IsRoc,
                Category = SegmentCategory.IndicatorCondition,
                DisplayName = "ROC Below",
                Order = order++,
                Parameters =
                [
                    new SegmentParameter
                    {
                        Name = "Condition",
                        Label = "Condition",
                        Type = ParameterType.String,
                        Value = "Below",
                        IsRequired = true
                    },
                    new SegmentParameter
                    {
                        Name = "Threshold",
                        Label = "Threshold",
                        Type = ParameterType.Double,
                        Value = condition.Value.Value,
                        IsRequired = true
                    }
                ]
            },
            _ => null
        };
    }

    // ========================================================================
    // INTERNAL TYPES
    // ========================================================================

    private enum ConditionType
    {
        Breakout,
        Pullback,
        PriceAbove,
        PriceBelow,
        AboveVwap,
        BelowVwap,
        EmaAbove,
        EmaBelow,
        EmaBetween,
        RsiOversold,
        RsiOverbought,
        AdxAbove,
        MacdBullish,
        MacdBearish,
        DiPositive,
        DiNegative,
        MomentumAbove,
        MomentumBelow,
        RocAbove,
        RocBelow
    }

    private sealed class OrderedCondition(
        ConditionType type,
        double? value = null,
        int? period = null,
        int? period2 = null)
    {
        public ConditionType Type { get; } = type;
        public double? Value { get; } = value;
        public int? Period { get; } = period;
        public int? Period2 { get; } = period2;
    }

    private enum CloseOrderType
    {
        None,
        CloseLong,
        CloseShort
    }

    private sealed class ParseContext
    {
        public string? Symbol { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
        public int Quantity { get; set; } = 1;
        public double? EntryPrice { get; set; }
        public double? TakeProfitPrice { get; set; }
        public double? StopLossPrice { get; set; }
        public double? TrailingStopLossPercent { get; set; }
        public List<OrderedCondition> OrderedConditions { get; } = [];
        public TradingSession? Session { get; set; }
        public TimeOnly? ClosePositionTime { get; set; }
        public bool CloseOnlyIfProfitable { get; set; } = true;
        public bool IsBuy { get; set; } = true;
        public CloseOrderType CloseOrderType { get; set; } = CloseOrderType.None;
        public TimeInForce? TimeInForce { get; set; }
        public bool? OutsideRth { get; set; }
        public bool? AllOrNone { get; set; }
        public OrderType OrderType { get; set; } = Enums.OrderType.Limit; // Default to LIMIT for safe premarket/after-hours execution
        public bool RepeatEnabled { get; set; } = false;
    }
}

/// <summary>
/// Exception thrown when parsing an IdiotScript fails.
/// </summary>
public class IdiotScriptException : Exception
{
    public IdiotScriptException(string message) : base(message) { }
    public IdiotScriptException(string message, Exception inner) : base(message, inner) { }
}
