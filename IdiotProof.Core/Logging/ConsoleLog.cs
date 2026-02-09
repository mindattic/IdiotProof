// ============================================================================
// ConsoleLog - Centralized Console Output with Consistent Formatting
// ============================================================================
// All console output goes through this class to ensure consistent formatting:
//   [hh:mm:ss AM/PM]    [Category]         Message
//
// Format:
// - Timestamp in brackets (Eastern Time, 12-hour format)
// - 4 spaces after timestamp
// - Category in brackets, padded to 18 chars (including brackets)
// - Message text
// ============================================================================

using IdiotProof.Core.Models;

namespace IdiotProof.Logging;

/// <summary>
/// Centralized console logging with consistent timestamp and category formatting.
/// All output follows the pattern: [timestamp]    [Category]         Message
/// </summary>
public static class ConsoleLog
{
    /// <summary>
    /// Category name width (including brackets).
    /// Longest category is "HistoryCache" = 14 chars + 2 brackets = 16 chars.
    /// Use 18 for padding.
    /// </summary>
    private const int CategoryWidth = 18;

    /// <summary>
    /// Write a log message with timestamp and category.
    /// </summary>
    /// <param name="category">The log category (e.g., "History", "DATA", "LEARN").</param>
    /// <param name="message">The message to log.</param>
    public static void Write(string category, string message)
    {
        var formatted = FormatLine(category, message);
        Console.WriteLine(formatted);
    }

    /// <summary>
    /// Write a log message with timestamp, category, and console color.
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="color">Console color for the message.</param>
    public static void Write(string category, string message, ConsoleColor color)
    {
        var formatted = FormatLine(category, message);
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(formatted);
        Console.ForegroundColor = originalColor;
    }

    /// <summary>
    /// Write a log message with timestamp and category, including a symbol prefix.
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <param name="symbol">The ticker symbol to include.</param>
    /// <param name="message">The message to log.</param>
    public static void Write(string category, string symbol, string message)
    {
        Write(category, $"[{symbol}] {message}");
    }

    /// <summary>
    /// Write a warning message (yellow).
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <param name="message">The warning message.</param>
    public static void Warn(string category, string message)
    {
        Write(category, $"WARN: {message}", ConsoleColor.Yellow);
    }

    /// <summary>
    /// Write an error message (red).
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <param name="message">The error message.</param>
    public static void Error(string category, string message)
    {
        Write(category, $"ERROR: {message}", ConsoleColor.Red);
    }

    /// <summary>
    /// Write a success message (green).
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <param name="message">The success message.</param>
    public static void Success(string category, string message)
    {
        Write(category, message, ConsoleColor.Green);
    }

    /// <summary>
    /// Write a plain message (no category, just timestamp + message).
    /// If the message starts with [CATEGORY], it will be parsed and formatted consistently.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Info(string message)
    {
        // Check if message starts with [CATEGORY] pattern and format accordingly
        if (message.StartsWith('['))
        {
            int endBracket = message.IndexOf(']');
            if (endBracket > 1 && endBracket < 20) // Reasonable category length
            {
                string category = message[1..endBracket];
                string rest = message[(endBracket + 1)..].TrimStart();
                Write(category, rest);
                return;
            }
        }
        
        // No category found - just output with timestamp
        Console.WriteLine($"{TimeStamp.NowBracketed}    {message}");
    }

    /// <summary>
    /// Write a blank line (no timestamp).
    /// </summary>
    public static void BlankLine()
    {
        Console.WriteLine();
    }

    /// <summary>
    /// Format a log line with consistent timestamp and category padding.
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <param name="message">The message.</param>
    /// <returns>Formatted log line.</returns>
    public static string FormatLine(string category, string message)
    {
        var bracketedCategory = $"[{category}]";
        var paddedCategory = bracketedCategory.PadRight(CategoryWidth);
        return $"{TimeStamp.NowBracketed}    {paddedCategory}{message}";
    }

    /// <summary>
    /// Format a log line with a specific timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp string (already bracketed).</param>
    /// <param name="category">The log category.</param>
    /// <param name="message">The message.</param>
    /// <returns>Formatted log line.</returns>
    public static string FormatLine(string timestamp, string category, string message)
    {
        var bracketedCategory = $"[{category}]";
        var paddedCategory = bracketedCategory.PadRight(CategoryWidth);
        return $"{timestamp}    {paddedCategory}{message}";
    }

    // ========================================================================
    // Predefined Category Methods for Common Categories
    // ========================================================================

    /// <summary>
    /// Log a History-related message.
    /// </summary>
    public static void History(string message) => Write("History", message);

    /// <summary>
    /// Log a History-related message with symbol.
    /// </summary>
    public static void History(string symbol, string message) => Write("History", symbol, message);

    /// <summary>
    /// Log a HistoryCache-related message.
    /// </summary>
    public static void HistoryCache(string message) => Write("HistoryCache", message);

    /// <summary>
    /// Log a HistoryCache-related message with symbol.
    /// </summary>
    public static void HistoryCache(string symbol, string message) => Write("HistoryCache", symbol, message);

    /// <summary>
    /// Log a DATA-related message.
    /// </summary>
    public static void Data(string message) => Write("Data", message);

    /// <summary>
    /// Log a LEARN-related message.
    /// </summary>
    public static void Learn(string message) => Write("Learn", message);

    /// <summary>
    /// Log an IBKR-related message.
    /// </summary>
    public static void Ibkr(string message) => Write("IBKR", message);

    /// <summary>
    /// Log a Strategy-related message.
    /// </summary>
    public static void Strategy(string message) => Write("Strategy", message);

    /// <summary>
    /// Log a Strategy-related message with symbol.
    /// </summary>
    public static void Strategy(string symbol, string message) => Write("Strategy", symbol, message);

    /// <summary>
    /// Log a Trade-related message.
    /// </summary>
    public static void Trade(string message) => Write("Trade", message);

    /// <summary>
    /// Log a Trade-related message with color.
    /// </summary>
    public static void Trade(string message, ConsoleColor color) => Write("Trade", message, color);

    /// <summary>
    /// Log an Order-related message.
    /// </summary>
    public static void Order(string message) => Write("Order", message);

    /// <summary>
    /// Log a Session-related message.
    /// </summary>
    public static void Session(string message) => Write("Session", message);

    /// <summary>
    /// Log an Indicator-related message.
    /// </summary>
    public static void Indicator(string message) => Write("Indicator", message);

    /// <summary>
    /// Log a Profile-related message.
    /// </summary>
    public static void Profile(string message) => Write("Profile", message);

    /// <summary>
    /// Log a Backtest-related message.
    /// </summary>
    public static void Backtest(string message) => Write("Backtest", message);

    /// <summary>
    /// Log a Manager-related message.
    /// </summary>
    public static void Manager(string message) => Write("Manager", message);

    /// <summary>
    /// Log a Validator-related message.
    /// </summary>
    public static void Validator(string message) => Write("Validator", message);
}
