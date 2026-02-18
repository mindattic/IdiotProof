// ============================================================================
// Pre-Market Strategies for 02/17/2026
// ============================================================================
// Based on pro trader analysis - "No Break, No Trade" pattern
// 
// These strategies use the BreakoutPullbackTracker state machine:
// Waiting → BrokeOut → PullingBack → Confirmed → Entry
//
// NEW FEATURES USED:
// - HoldsAbove(): Dual support level confirmation (VWAP + specific price)
// - TakeProfit(T1, T2, T3): Multi-target scaling out
// - MultiTargetExitManager: Handles partial exits automatically
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
        return Stock.Ticker("NCI")
            .Name("NCI Breakout-Pullback")
            .Session(TradingSession.RTH)
            .Breakout(3.68)              // Wait for break above $3.68
            .Pullback()                  // Then wait for pullback
            .IsAboveVwap()               // Must hold VWAP
            .Long()
            .TakeProfit(5.00, 6.50)      // T1: $5.00, T2: $6.50
            .StopLoss(3.50)              // Below VWAP invalidation
            .Repeat()                    // Re-enter if setup forms again
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
    .TakeProfit(5.00, 6.50)  // T1: $5.00 (+36%), T2: $6.50 (+77%)
    .StopLoss(3.50)          // Below VWAP invalidation
    .Repeat()                // Re-enter if setup forms again
";

    /// <summary>
    /// ERNA - After-hours momentum continuation
    /// Pattern: Breakout pullback with dual support
    /// NOW USES: HoldsAbove() for dual support confirmation!
    /// </summary>
    public static StrategyDefinition ERNA()
    {
        return Stock.Ticker("ERNA")
            .Name("ERNA AH Momentum")
            .Session(TradingSession.Premarket)
            .Breakout(0.52)              // Wait for break above $0.52
            .Pullback()                  // Then wait for pullback
            .IsAboveVwap()               // Must hold VWAP
            .HoldsAbove(0.48)            // AND must hold above $0.48!
            .Long()
            .TakeProfit(0.66, 0.88)      // T1: $0.66, T2: $0.88
            .StopLoss(0.46)              // Below $0.48 invalidation
            .Repeat()
            .Build();
    }

    /// <summary>
    /// ERNA IdiotScript representation.
    /// </summary>
    public static string ERNA_Script() => @"
// ERNA - After-hours momentum continuation  
// Trigger: Break over $0.52
// Confirmation: Pullback + VWAP hold + Hold $0.48 (DUAL SUPPORT!)
// Rule: NO BREAK, NO TRADE

Ticker(ERNA)
    .Name(""ERNA AH Momentum"")
    .Session(IS.PREMARKET)
    .Breakout(0.52)          // Wait for break above $0.52
    .Pullback()              // Wait for pullback
    .IsAboveVwap()           // Must hold VWAP
    .HoldsAbove(0.48)        // AND must hold above $0.48! (dual support)
    .Long()
    .TakeProfit(0.66, 0.88)  // T1: $0.66 (+27%), T2: $0.88 (+69%)
    .StopLoss(0.46)          // Below $0.48 invalidation
    .Repeat()
";

    /// <summary>
    /// SUNE - Coiled wedge breakout
    /// Pattern: Wedge breakout with specific support
    /// NOW USES: TakeProfit(T1, T2, T3) for three targets!
    /// </summary>
    public static StrategyDefinition SUNE()
    {
        return Stock.Ticker("SUNE")
            .Name("SUNE Wedge Breakout")
            .Session(TradingSession.RTH)
            .Breakout(2.42)              // Wait for break above $2.42
            .Pullback()                  // Then wait for pullback
            .HoldsAbove(2.30)            // Must hold above $2.30
            .Long()
            .TakeProfit(2.85, 3.20, 4.20) // T1: $2.85, T2: $3.20, T3: $4.20!
            .StopLoss(2.25)              // Below $2.30 invalidation
            .Repeat()
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
    .Breakout(2.42)              // Wait for break above $2.42
    .Pullback()                  // Wait for pullback
    .HoldsAbove(2.30)            // Must hold above $2.30
    .Long()
    .TakeProfit(2.85, 3.20, 4.20) // T1: +18%, T2: +32%, T3: +73%
    .StopLoss(2.25)              // Below $2.30 invalidation
    .Repeat()
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
    /// Creates exit managers for all strategies with multi-target support.
    /// </summary>
    public static Dictionary<string, MultiTargetExitManager> CreateExitManagers()
    {
        var managers = new Dictionary<string, MultiTargetExitManager>();

        foreach (var strategy in GetAllStrategies())
        {
            if (strategy.HasMultipleTargets)
            {
                var manager = new MultiTargetExitManager(strategy.Symbol);
                managers[strategy.Symbol] = manager;
            }
        }

        return managers;
    }

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
