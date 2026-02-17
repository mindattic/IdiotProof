// ============================================================================
// Pre-Market Strategies for 02/17/2026
// ============================================================================
// Based on pro trader analysis - "No Break, No Trade" pattern
// 
// These strategies use the BreakoutPullbackTracker state machine:
// Waiting → BrokeOut → PullingBack → Confirmed → Entry
// ============================================================================

using IdiotProof.Scripting;
using IdiotProof.Shared;

namespace IdiotProof.Strategies;

/// <summary>
/// Pre-built strategies for 02/17/2026 based on pro trader analysis.
/// </summary>
public static class Strategies_20260217
{
    /// <summary>
    /// NCI - Bullish continuation
    /// Pattern: Breakout pullback
    /// </summary>
    public static StrategyDefinition NCI()
    {
        return BreakoutPullbackStrategyBuilder.Create("NCI")
            .WithName("NCI Breakout-Pullback")
            .WithSession(TradingSession.RTH)
            .Bias("Bullish continuation")
            .Pattern("Breakout pullback")
            .Trigger(3.68)                      // Break over $3.68
            .Support(0)                         // VWAP (dynamic - will use IsAboveVwap())
            .Invalidation(3.50)                 // Below VWAP fails the pattern
            .Targets(5.00, 6.50)                // T1: $5.00, T2: $6.50
            .Build();
    }
    
    /// <summary>
    /// NCI IdiotScript representation.
    /// </summary>
    public static string NCI_Script() => @"
// NCI - Bullish continuation
// Trigger: Break over $3.68
// Confirmation: Pullback + VWAP hold
// Rule: NO BREAK, NO TRADE

Ticker(NCI)
    .Name(""NCI Breakout-Pullback"")
    .Session(IS.RTH)
    .Breakout(3.68)          // Wait for break above $3.68
    .Pullback()              // Then wait for pullback
    .IsAboveVwap()           // Must hold VWAP
    .Long()
    .TakeProfit(5.00)        // T1: $5.00 (+36%)
    .StopLoss(3.50)          // Below VWAP invalidation
    .Repeat()                // Re-enter if setup forms again
    
// Manual scale targets:
// T2: $6.50 (+77%) - half position
";

    /// <summary>
    /// ERNA - After-hours momentum continuation
    /// Pattern: Breakout pullback with dual support
    /// </summary>
    public static StrategyDefinition ERNA()
    {
        return BreakoutPullbackStrategyBuilder.Create("ERNA")
            .WithName("ERNA AH Momentum")
            .WithSession(TradingSession.Premarket)  // AH momentum play
            .Bias("After-hours momentum continuation")
            .Pattern("Breakout pullback with dual support")
            .Trigger(0.52)                      // Break over $0.52
            .Support(0.48)                      // Must hold $0.48 AND VWAP
            .Invalidation(0.46)                 // Below $0.48 fails
            .Targets(0.66, 0.88)                // T1: $0.66, T2: $0.88
            .Build();
    }
    
    /// <summary>
    /// ERNA IdiotScript representation.
    /// </summary>
    public static string ERNA_Script() => @"
// ERNA - After-hours momentum continuation  
// Trigger: Break over $0.52
// Confirmation: Pullback + VWAP hold + Hold $0.48
// Rule: NO BREAK, NO TRADE

Ticker(ERNA)
    .Name(""ERNA AH Momentum"")
    .Session(IS.PREMARKET)
    .Breakout(0.52)          // Wait for break above $0.52
    .Pullback(0.48)          // Pullback must hold above $0.48
    .IsAboveVwap()           // AND must hold VWAP
    .Long()
    .TakeProfit(0.66)        // T1: $0.66 (+27%)
    .StopLoss(0.46)          // Below $0.48 invalidation
    .Repeat()

// Manual scale targets:
// T2: $0.88 (+69%) - runner
";

    /// <summary>
    /// SUNE - Coiled wedge breakout
    /// Pattern: Wedge breakout with specific support
    /// </summary>
    public static StrategyDefinition SUNE()
    {
        return BreakoutPullbackStrategyBuilder.Create("SUNE")
            .WithName("SUNE Wedge Breakout")
            .WithSession(TradingSession.RTH)
            .Bias("Coiled wedge breakout")
            .Pattern("Wedge breakout with bounce confirmation")
            .Trigger(2.42)                      // Break over $2.42
            .Support(2.30)                      // Must hold $2.30
            .Invalidation(2.25)                 // Below $2.30 fails
            .Targets(2.85, 3.20, 4.20)          // T1: $2.85, T2: $3.20, T3: $4.20
            .Build();
    }
    
    /// <summary>
    /// SUNE IdiotScript representation.
    /// </summary>
    public static string SUNE_Script() => @"
// SUNE - Coiled wedge breakout
// Trigger: Break over $2.42
// Confirmation: Pullback + Hold $2.30
// Rule: NO BREAK, NO TRADE

Ticker(SUNE)
    .Name(""SUNE Wedge Breakout"")
    .Session(IS.RTH)
    .Breakout(2.42)          // Wait for break above $2.42
    .Pullback(2.30)          // Pullback must hold above $2.30
    .Long()
    .TakeProfit(2.85)        // T1: $2.85 (+18%)
    .StopLoss(2.25)          // Below $2.30 invalidation
    .Repeat()

// Manual scale targets:
// T2: $3.20 (+32%) - half position
// T3: $4.20 (+73%) - runner
";

    /// <summary>
    /// Gets all strategies for the day.
    /// </summary>
    public static List<StrategyDefinition> GetAllStrategies() =>
    [
        NCI(),
        ERNA(),
        SUNE()
    ];
    
    /// <summary>
    /// Prints strategy cards in the pro trader format.
    /// </summary>
    public static void PrintStrategyCards()
    {
        Console.WriteLine(GetNCICard());
        Console.WriteLine(GetERNACard());
        Console.WriteLine(GetSUNECard());
    }
    
    public static string GetNCICard() => @"
╔═══════════════════════════════════════════════════════════════════════════════╗
║  NCI        Bullish continuation                                              ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║  Pattern:   Breakout pullback                                                 ║
║                                                                               ║
║  Trigger:   Break over $3.68                                                  ║
║                                                                               ║
║  Confirmation:                                                                ║
║    * Pullback after breakout                                                  ║
║    * Holds / reclaims VWAP                                                    ║
║                                                                               ║
║  Entry:     On confirmed VWAP hold after breakout                             ║
║                                                                               ║
║  Targets:                                                                     ║
║    T1: $5.00  (+36%)                                                          ║
║    T2: $6.50  (+77%)                                                          ║
║                                                                               ║
║  Invalidation:                                                                ║
║    * No break over $3.68                                                      ║
║    * Failed VWAP hold after breakout                                          ║
║                                                                               ║
║  Rule: NO BREAK, NO TRADE.                                                    ║
╚═══════════════════════════════════════════════════════════════════════════════╝";

    public static string GetERNACard() => @"
╔═══════════════════════════════════════════════════════════════════════════════╗
║  ERNA       After-hours momentum continuation                                 ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║  Pattern:   Breakout pullback with dual support                               ║
║                                                                               ║
║  Trigger:   Break over $0.52                                                  ║
║                                                                               ║
║  Confirmation:                                                                ║
║    * Pullback after breakout                                                  ║
║    * Holds / reclaims VWAP                                                    ║
║    * Holds above $0.48                                                        ║
║                                                                               ║
║  Entry:     On confirmation above VWAP and $0.48 after breakout               ║
║                                                                               ║
║  Targets:                                                                     ║
║    T1: $0.66  (+27%)                                                          ║
║    T2: $0.88  (+69%)                                                          ║
║                                                                               ║
║  Invalidation:                                                                ║
║    * No break over $0.52                                                      ║
║    * Loss of VWAP and $0.48 after breakout                                    ║
║                                                                               ║
║  Rule: NO BREAK, NO TRADE.                                                    ║
╚═══════════════════════════════════════════════════════════════════════════════╝";

    public static string GetSUNECard() => @"
╔═══════════════════════════════════════════════════════════════════════════════╗
║  SUNE       Coiled wedge breakout                                             ║
╠═══════════════════════════════════════════════════════════════════════════════╗
║  Pattern:   Wedge breakout with bounce confirmation                           ║
║                                                                               ║
║  Trigger:   Break over $2.42                                                  ║
║                                                                               ║
║  Confirmation:                                                                ║
║    * Pullback after breakout                                                  ║
║    * Holds above $2.30                                                        ║
║                                                                               ║
║  Entry:     On confirmed bounce above $2.30 after breakout                    ║
║                                                                               ║
║  Targets:                                                                     ║
║    T1: $2.85  (+18%)                                                          ║
║    T2: $3.20  (+32%)                                                          ║
║    T3: $4.20  (+73%)                                                          ║
║                                                                               ║
║  Invalidation:                                                                ║
║    * No break over $2.42                                                      ║
║    * Breakdown below $2.30 after breakout                                     ║
║                                                                               ║
║  Rule: NO BREAK, NO TRADE.                                                    ║
╚═══════════════════════════════════════════════════════════════════════════════╝";
}
