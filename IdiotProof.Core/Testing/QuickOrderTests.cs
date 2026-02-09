// ============================================================================
// Quick Order Tests - Simple tests for BUY/SELL/SHORT/COVER validation
// ============================================================================
//
// PURPOSE:
// Simple, quick tests that can verify order execution works correctly.
// These are meant to be run during RTH and require IBKR connection.
//
// USAGE:
// Call RunQuickOrderTests() from the main program menu.
// The tests will:
// 1. Let you pick from a list of cheap, liquid stocks
// 2. Execute a BUY, verify the fill
// 3. Execute a SELL, verify the fill
// 4. Optionally execute SHORT and COVER (if shortable)
// 5. Report results
//
// COST:
// Minimal - uses 1 share of cheap stocks (~$10 round trip cost)
//
// ============================================================================

namespace IdiotProof.Testing;

/// <summary>
/// Quick order tests for validating BUY/SELL/SHORT/COVER.
/// </summary>
public static class QuickOrderTests
{
    /// <summary>
    /// Interactive prompt for running live trading tests.
    /// </summary>
    public static void ShowTestMenu()
    {
        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  LIVE TRADING TESTS                                            ║");
        Console.WriteLine("║                                                                ║");
        Console.WriteLine("║  These tests execute REAL orders to validate order execution.  ║");
        Console.WriteLine("║  They use 1 share of a cheap stock to minimize cost/risk.      ║");
        Console.WriteLine("║                                                                ║");
        Console.WriteLine("║  Requirements:                                                 ║");
        Console.WriteLine("║  - Must run during RTH (9:30 AM - 4:00 PM ET)                  ║");
        Console.WriteLine("║  - Connected to IBKR (paper trading recommended)              ║");
        Console.WriteLine("║  - Account approved for short selling (for SHORT/COVER tests) ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Available Test Stocks (cheapest first):                       ║");
        
        for (int i = 0; i < Math.Min(LiveTradingTestConfig.TestableStocks.Length, 10); i++)
        {
            var symbol = LiveTradingTestConfig.TestableStocks[i];
            Console.WriteLine($"║    {i + 1}. {symbol,-6}                                              ║");
        }
        
        Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Options:                                                      ║");
        Console.WriteLine("║    [1-9] Run tests with selected stock                         ║");
        Console.WriteLine("║    [A]   Run all with default stock (SOFI)                     ║");
        Console.WriteLine("║    [L]   Long only (BUY/SELL, skip SHORT/COVER)                ║");
        Console.WriteLine("║    [Q]   Cancel                                                ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.Write("\nSelect option: ");
    }

    /// <summary>
    /// Gets a stock symbol from user input.
    /// </summary>
    public static (string? symbol, bool skipShorts, bool cancel) GetUserSelection()
    {
        var input = Console.ReadLine()?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(input) || input == "Q")
        {
            return (null, false, true);
        }

        if (input == "A")
        {
            return (LiveTradingTestConfig.DefaultTestStock, false, false);
        }

        if (input == "L")
        {
            return (LiveTradingTestConfig.DefaultTestStock, true, false);
        }

        if (int.TryParse(input, out int index) && 
            index >= 1 && 
            index <= LiveTradingTestConfig.TestableStocks.Length)
        {
            return (LiveTradingTestConfig.TestableStocks[index - 1], false, false);
        }

        // If they typed a ticker directly, use it
        if (input.Length >= 1 && input.Length <= 5 && input.All(char.IsLetter))
        {
            return (input, false, false);
        }

        return (null, false, true);
    }

    /// <summary>
    /// Prints a safety warning before running tests.
    /// </summary>
    public static bool ConfirmExecution(string symbol, bool isPaper)
    {
        Console.WriteLine();
        
        if (!isPaper)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  !!! WARNING: LIVE ACCOUNT DETECTED !!!                        ║");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  You are about to execute REAL trades on a LIVE account.       ║");
            Console.WriteLine("║  This will cost real money (small amount, but real).           ║");
            Console.WriteLine("║                                                                ║");
            Console.WriteLine("║  Are you SURE you want to continue?                            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.Write("\nType 'YES' to confirm (or anything else to cancel): ");
            var confirm = Console.ReadLine()?.Trim();
            return confirm == "YES";
        }
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"About to run tests with {symbol} (1 share)...");
        Console.ResetColor();
        Console.Write("Press ENTER to continue or 'Q' to cancel: ");
        var input = Console.ReadLine()?.Trim().ToUpperInvariant();
        return input != "Q";
    }

    /// <summary>
    /// Prints the post-test summary and recommendations.
    /// </summary>
    public static void PrintRecommendations(LiveTestSummary summary)
    {
        Console.WriteLine();
        
        if (summary.AllPassed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  ALL TESTS PASSED                                              ║");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Your IBKR connection and order execution are working!         ║");
            Console.WriteLine("║  BUY, SELL, SHORT, and COVER all executed successfully.        ║");
            Console.WriteLine("║                                                                ║");
            Console.WriteLine("║  Next steps:                                                   ║");
            Console.WriteLine("║  - Set up your watchlist with tickers you want to trade        ║");
            Console.WriteLine("║  - Run AI learning to build trading profiles                   ║");
            Console.WriteLine("║  - Start autonomous trading with confidence                    ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  SOME TESTS FAILED                                             ║");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
            Console.ResetColor();
            
            // Check for common issues
            var failedTests = summary.Results.Where(r => !r.Passed).ToList();
            
            foreach (var failed in failedTests)
            {
                Console.WriteLine($"║  - {failed.TestName}: {failed.Message,-40} ║");
                
                if (failed.TestName == "SHORT" && 
                    (failed.ErrorDetails?.Contains("short", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("║    TIP: Stock may not be shortable. Try F or NIO.             ║");
                    Console.ResetColor();
                }
            }
            
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        }
        
        Console.WriteLine();
    }
}
