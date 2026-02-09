// ============================================================================
// Live Trading Test Configuration
// ============================================================================
//
// Configuration for running live order execution tests during RTH.
// Tests BUY, SELL (close long), SHORT, and COVER (close short) operations.
//
// SAFETY MEASURES:
// 1. Uses very small quantities (1-5 shares)
// 2. Uses market orders for quick fills
// 3. Includes timeout protection
// 4. Cleans up all positions after tests
// 5. Targets cheap, liquid stocks to minimize cost
//
// REQUIREMENTS:
// - Must run during RTH (9:30 AM - 4:00 PM ET)
// - Requires IBKR connection (paper or live)
// - Stock must be shortable (marginable)
//
// ============================================================================

namespace IdiotProof.Testing;

/// <summary>
/// Configuration for live trading tests.
/// </summary>
public sealed class LiveTradingTestConfig
{
    /// <summary>
    /// List of cheap, liquid stocks suitable for testing.
    /// Ordered by typical price (lowest first).
    /// </summary>
    public static readonly string[] TestableStocks =
    [
        "SNDL",   // Sundial Growers ~$1-2 - Cannabis, very cheap
        "CLOV",   // Clover Health ~$1-3 - Healthcare tech
        "NIO",    // NIO Inc ~$5-8 - EV, very liquid
        "F",      // Ford Motor ~$10-12 - Auto, extremely liquid
    ];

    /// <summary>
    /// Default stock to use for testing if no preference specified.
    /// SNDL is chosen because it's:
    /// - Very cheap (~$1-2) to minimize test cost
    /// - Liquid enough for fast fills
    /// </summary>
    public const string DefaultTestStock = "SNDL";

    /// <summary>
    /// Target dollar allocation for tests (~$20 worth of shares).
    /// </summary>
    public const decimal TargetAllocation = 20m;

    /// <summary>
    /// Minimum quantity (at least 1 share).
    /// </summary>
    public const int MinTestQuantity = 1;

    /// <summary>
    /// Maximum quantity allowed for safety.
    /// </summary>
    public const int MaxTestQuantity = 50;

    /// <summary>
    /// Timeout in seconds for order fills.
    /// </summary>
    public const int OrderFillTimeoutSeconds = 30;

    /// <summary>
    /// Delay between tests in milliseconds.
    /// </summary>
    public const int DelayBetweenTestsMs = 2000;

    /// <summary>
    /// Maximum acceptable slippage percentage.
    /// If fill price differs from expected by more than this, log a warning.
    /// </summary>
    public const double MaxSlippagePercent = 1.0;

    /// <summary>
    /// Gets the test stock symbol to use.
    /// </summary>
    public string Symbol { get; init; } = DefaultTestStock;

    /// <summary>
    /// Gets the quantity to trade (calculated from price, or override).
    /// If 0, will be calculated from TargetAllocation and current price.
    /// </summary>
    public int Quantity { get; init; } = 0;

    /// <summary>
    /// Calculates quantity based on target allocation and price.
    /// </summary>
    public static int CalculateQuantity(double price)
    {
        if (price <= 0) return MinTestQuantity;
        int qty = (int)Math.Floor((double)TargetAllocation / price);
        return Math.Clamp(qty, MinTestQuantity, MaxTestQuantity);
    }

    /// <summary>
    /// Gets whether to run the full test suite or just connectivity tests.
    /// </summary>
    public bool FullSuite { get; init; } = true;

    /// <summary>
    /// Gets whether to skip the SHORT/COVER tests (some stocks aren't shortable).
    /// </summary>
    public bool SkipShortTests { get; init; } = false;

    /// <summary>
    /// Gets whether to automatically clean up positions after test failures.
    /// </summary>
    public bool AutoCleanup { get; init; } = true;

    /// <summary>
    /// Validates that the configuration is safe to use.
    /// </summary>
    public void Validate()
    {
        // Quantity of 0 means calculate from price
        if (Quantity < 0)
            throw new ArgumentException("Quantity cannot be negative");

        if (Quantity > MaxTestQuantity)
            throw new ArgumentException($"Quantity cannot exceed {MaxTestQuantity} for safety");

        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required");
    }
}

/// <summary>
/// Result of a single test operation.
/// </summary>
public sealed class TestResult
{
    public string TestName { get; init; } = "";
    public bool Passed { get; init; }
    public string Message { get; init; } = "";
    public double? FillPrice { get; init; }
    public double? ExpectedPrice { get; init; }
    public TimeSpan Duration { get; init; }
    public int? OrderId { get; init; }
    public string? ErrorDetails { get; init; }

    public static TestResult Pass(string testName, string message, double? fillPrice = null, int? orderId = null, TimeSpan? duration = null)
    {
        return new TestResult
        {
            TestName = testName,
            Passed = true,
            Message = message,
            FillPrice = fillPrice,
            OrderId = orderId,
            Duration = duration ?? TimeSpan.Zero
        };
    }

    public static TestResult Fail(string testName, string message, string? errorDetails = null)
    {
        return new TestResult
        {
            TestName = testName,
            Passed = false,
            Message = message,
            ErrorDetails = errorDetails
        };
    }
}

/// <summary>
/// Summary of all live trading test results.
/// </summary>
public sealed class LiveTestSummary
{
    public List<TestResult> Results { get; } = [];
    public int TotalTests => Results.Count;
    public int PassedTests => Results.Count(r => r.Passed);
    public int FailedTests => Results.Count(r => !r.Passed);
    public bool AllPassed => FailedTests == 0;
    public TimeSpan TotalDuration { get; set; }
    public string Symbol { get; set; } = "";
    public DateTime TestStartTime { get; set; }
    public DateTime TestEndTime { get; set; }

    /// <summary>
    /// Calculates total cost of all test trades (fills only).
    /// </summary>
    public double TotalTradeCost => Results
        .Where(r => r.FillPrice.HasValue)
        .Sum(r => r.FillPrice!.Value);

    public void AddResult(TestResult result) => Results.Add(result);

    /// <summary>
    /// Prints a formatted summary to console.
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  LIVE TRADING TEST RESULTS                                     ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Symbol: {Symbol,-20} Start: {TestStartTime:HH:mm:ss}           ║");
        Console.WriteLine($"║  Duration: {TotalDuration.TotalSeconds:F1}s                     End: {TestEndTime:HH:mm:ss}             ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");

        foreach (var result in Results)
        {
            var status = result.Passed ? "[OK]" : "[FAIL]";
            var color = result.Passed ? ConsoleColor.Green : ConsoleColor.Red;
            var priceStr = result.FillPrice.HasValue ? $"@ ${result.FillPrice:F2}" : "";
            var timeStr = result.Duration.TotalMilliseconds > 0 ? $"({result.Duration.TotalMilliseconds:F0}ms)" : "";

            Console.ForegroundColor = color;
            Console.Write($"║  {status} ");
            Console.ResetColor();
            Console.WriteLine($"{result.TestName,-20} {priceStr,-12} {timeStr,-10}       ║");

            if (!result.Passed && !string.IsNullOrEmpty(result.ErrorDetails))
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"║       -> {result.ErrorDetails,-50}  ║");
                Console.ResetColor();
            }
        }

        Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
        Console.ForegroundColor = AllPassed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"║  TOTAL: {PassedTests}/{TotalTests} passed                                            ║");
        Console.ResetColor();
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
