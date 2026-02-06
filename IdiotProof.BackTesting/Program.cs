// ============================================================================
// IdiotProof.BackTesting - Strategy Optimization Console
// ============================================================================
//
// Usage:
//   IdiotProof.BackTesting <ticker> <date> [options]
//
// Examples:
//   IdiotProof.BackTesting AAPL 2024-01-15
//   IdiotProof.BackTesting TSLA 2024-02-20 --synthetic
//   IdiotProof.BackTesting NVDA 2024-03-10 --output strategy.idiot
//   IdiotProof.BackTesting AAPL 2024-01-15 --analysis full
//   IdiotProof.BackTesting AAPL 2024-01-15 --autonomous --learn
//
// ============================================================================

using IdiotProof.BackTesting.Analysis;
using IdiotProof.BackTesting.Learning;
using IdiotProof.BackTesting.Models;
using IdiotProof.BackTesting.Optimization;
using IdiotProof.BackTesting.Services;

namespace IdiotProof.BackTesting;

public static class Program
{
    private const string DataDirectory = "Data";

    public static async Task<int> Main(string[] args)
    {
        PrintBanner();

        // Check for --profile flag with just a ticker
        if (args.Length >= 1 && (args.Contains("--profile") || args.Contains("-p")))
        {
            return ShowTickerProfile(args[0].ToUpperInvariant());
        }

        // Check for --show-metadata flag with just a ticker
        if (args.Length >= 1 && (args.Contains("--show-metadata") || args.Contains("-sm")))
        {
            return ShowTickerMetadata(args[0].ToUpperInvariant());
        }

        // Parse arguments
        if (args.Length < 2)
        {
            PrintUsage();
            return await RunInteractiveMode();
        }

        string symbol = args[0].ToUpperInvariant();
        if (!DateOnly.TryParse(args[1], out var date))
        {
            Console.WriteLine($"[ERR] Invalid date format: {args[1]}");
            Console.WriteLine("      Use format: yyyy-MM-dd (e.g., 2024-01-15)");
            return 1;
        }

        bool useSynthetic = args.Contains("--synthetic") || args.Contains("-s");
        string? outputFile = GetArgValue(args, "--output") ?? GetArgValue(args, "-o");
        string? analysisMode = GetArgValue(args, "--analysis") ?? GetArgValue(args, "-a");
        bool runMonteCarlo = args.Contains("--monte-carlo") || args.Contains("-mc");
        bool runWalkForward = args.Contains("--walk-forward") || args.Contains("-wf");
        bool runAutonomous = args.Contains("--autonomous") || args.Contains("-auto");
        string? autonomousMode = GetArgValue(args, "--mode") ?? GetArgValue(args, "-m");
        bool learnFromResults = args.Contains("--learn") || args.Contains("-l");
        bool runMetadataAnalysis = args.Contains("--metadata") || args.Contains("-meta");
        string? daysToAnalyze = GetArgValue(args, "--days") ?? GetArgValue(args, "-d");

        // Run metadata analysis if requested
        if (runMetadataAnalysis)
        {
            int days = int.TryParse(daysToAnalyze, out var d) ? d : 30;
            return await RunMetadataAnalysis(symbol, date, useSynthetic, days);
        }

        // Run autonomous trading simulation if requested
        if (runAutonomous)
        {
            return await RunAutonomousSimulation(symbol, date, useSynthetic, autonomousMode, learnFromResults);
        }

        return await RunBacktest(symbol, date, useSynthetic, outputFile, analysisMode, runMonteCarlo, runWalkForward, learnFromResults);
    }

    private static int ShowTickerProfile(string symbol)
    {
        Console.WriteLine();
        Console.WriteLine($"[*] Loading profile for {symbol}...");

        var profileManager = new TickerProfileManager();
        Console.WriteLine($"    Profile directory: {profileManager.ProfileDirectory}");

        var profile = profileManager.Load(symbol);

        if (profile == null)
        {
            Console.WriteLine($"[!] No profile found for {symbol}.");
            Console.WriteLine("    Run backtests with --learn flag to generate a profile.");
            return 1;
        }

        var generator = new ProfileGenerator(profileManager);
        Console.WriteLine(generator.GenerateProfileReport(profile));

        return 0;
    }

    private static int ShowTickerMetadata(string symbol)
    {
        Console.WriteLine();
        Console.WriteLine($"[*] Loading historical metadata for {symbol}...");

        var metadataManager = new HistoricalMetadataManager();
        Console.WriteLine($"    Metadata directory: {metadataManager.MetadataDirectory}");

        var metadata = metadataManager.Load(symbol);

        if (metadata == null)
        {
            Console.WriteLine($"[!] No metadata found for {symbol}.");
            Console.WriteLine("    Run: IdiotProof.BackTesting {symbol} {date} --metadata");
            Console.WriteLine("    This will analyze historical data and extract patterns.");
            return 1;
        }

        var analyzer = new MetadataAnalyzer(metadataManager);
        Console.WriteLine(analyzer.GenerateMetadataReport(metadata));

        return 0;
    }

    private static async Task<int> RunInteractiveMode()
    {
        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine("| INTERACTIVE MODE                                 |");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();

        // Get ticker
        Console.Write("Enter ticker symbol: ");
        string? symbol = Console.ReadLine()?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("[ERR] Ticker symbol is required.");
            return 1;
        }

        // Get date
        Console.Write("Enter date (yyyy-MM-dd) or 'today': ");
        string? dateInput = Console.ReadLine()?.Trim();

        DateOnly date;
        if (dateInput?.Equals("today", StringComparison.OrdinalIgnoreCase) == true)
        {
            date = DateOnly.FromDateTime(DateTime.Today);
        }
        else if (!DateOnly.TryParse(dateInput, out date))
        {
            Console.WriteLine("[ERR] Invalid date format.");
            return 1;
        }

        // Ask about data source
        Console.Write("Use synthetic data for testing? (y/N): ");
        bool useSynthetic = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;

        // Ask about mode
        Console.Write("Run AutonomousTrading simulation? (y/N): ");
        bool runAutonomous = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;

        if (runAutonomous)
        {
            Console.Write("Mode (conservative/balanced/aggressive) [balanced]: ");
            string? modeInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(modeInput)) modeInput = "balanced";

            Console.Write("Save results to learning profile? (Y/n): ");
            bool learn = !Console.ReadLine()?.Trim().Equals("n", StringComparison.OrdinalIgnoreCase) == true;

            Console.WriteLine();
            return await RunAutonomousSimulation(symbol, date, useSynthetic, modeInput, learn);
        }

        // Ask about analysis
        Console.Write("Run full analysis (Monte Carlo + Walk-Forward)? (y/N): ");
        bool fullAnalysis = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;

        Console.WriteLine();
        return await RunBacktest(symbol, date, useSynthetic, null, 
            fullAnalysis ? "full" : null, fullAnalysis, fullAnalysis, false);
    }

    private static async Task<int> RunAutonomousSimulation(
        string symbol,
        DateOnly date,
        bool useSynthetic,
        string? modeStr,
        bool learnFromResults = false)
    {
        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine($"| AUTONOMOUS TRADING: {symbol} on {date:yyyy-MM-dd}");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();

        // Parse mode
        var mode = modeStr?.ToLowerInvariant() switch
        {
            "conservative" or "c" => AutonomousMode.Conservative,
            "aggressive" or "a" => AutonomousMode.Aggressive,
            _ => AutonomousMode.Balanced
        };

        Console.WriteLine($"[*] Mode: {mode}");
        if (learnFromResults)
            Console.WriteLine("[*] Learning: ENABLED - results will update ticker profile");

        // Load data
        Console.WriteLine("[*] Loading historical data...");

        IHistoricalDataProvider dataProvider = useSynthetic
            ? new SyntheticDataProvider()
            : new CsvHistoricalDataProvider(DataDirectory);

        BackTestSession session;
        try
        {
            session = await dataProvider.LoadSessionAsync(symbol, date);
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"[ERR] {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Use --synthetic flag for testing with generated data.");
            return 1;
        }

        // Print session info
        Console.WriteLine(session.ToString());

        // Create config
        var config = new AutonomousConfig
        {
            Mode = mode,
            Quantity = 100,
            AllowShort = true,
            AllowDirectionFlip = true
        };

        // Run simulation
        Console.WriteLine("[*] Running autonomous trading simulation...");
        Console.WriteLine($"    Entry thresholds: LONG >= {config.LongEntryThreshold}, SHORT <= {config.ShortEntryThreshold}");
        Console.WriteLine($"    Exit thresholds:  LONG < {config.LongExitThreshold}, SHORT > {config.ShortExitThreshold}");
        Console.WriteLine();

        var simulator = new AutonomousTradeSimulator(session, config);
        var progress = new Progress<string>(msg => Console.WriteLine($"    {msg}"));
        var result = simulator.Simulate(progress);

        // Print results
        Console.WriteLine();
        Console.WriteLine(result.ToString());

        // Print score timeline
        Console.WriteLine(simulator.GenerateScoreTimeline(result));

        // Learn from results if enabled
        if (learnFromResults && result.Trades.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("[*] Updating ticker profile with learned patterns...");

            var profileManager = new TickerProfileManager();
            var generator = new ProfileGenerator(profileManager);
            var profile = generator.GenerateFromAutonomousSimulation(result, session);

            Console.WriteLine($"[OK] Profile updated: {profile.TotalTrades} total trades, " +
                $"{profile.Confidence * 100:F0}% confidence");
            Console.WriteLine($"    Optimal Long Entry: >= {profile.OptimalLongEntryThreshold:F0}");

            if (profile.BestHours.Count > 0)
                Console.WriteLine($"    Best Hours: {string.Join(", ", profile.BestHours.Select(h => $"{h}:00"))}");
            if (profile.AvoidHours.Count > 0)
                Console.WriteLine($"    Avoid Hours: {string.Join(", ", profile.AvoidHours.Select(h => $"{h}:00"))}");
        }

        // Summary
        Console.WriteLine();
        if (result.TotalPnL > 0)
        {
            Console.WriteLine($"[OK] Profitable day! Net: ${result.TotalPnL:F2} ({result.WinRate:F0}% win rate)");
        }
        else if (result.TotalPnL < 0)
        {
            Console.WriteLine($"[!]  Losing day. Net: ${result.TotalPnL:F2} ({result.WinRate:F0}% win rate)");
        }
        else
        {
            Console.WriteLine("[*]  Break-even day.");
        }

        return 0;
    }

    private static async Task<int> RunBacktest(
        string symbol, 
        DateOnly date, 
        bool useSynthetic,
        string? outputFile,
        string? analysisMode = null,
        bool runMonteCarlo = false,
        bool runWalkForward = false,
        bool learnFromResults = false)
    {
        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine($"| BACKTEST: {symbol} on {date:yyyy-MM-dd}");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();

        // Load data
        Console.WriteLine("[*] Loading historical data...");
        
        IHistoricalDataProvider dataProvider = useSynthetic
            ? new SyntheticDataProvider()
            : new CsvHistoricalDataProvider(DataDirectory);

        BackTestSession session;
        try
        {
            session = await dataProvider.LoadSessionAsync(symbol, date);
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"[ERR] {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("To use this tool, you need historical data in CSV format.");
            Console.WriteLine($"Place files in: {Path.GetFullPath(DataDirectory)}");
            Console.WriteLine($"Naming: {symbol}_yyyy-MM-dd.csv");
            Console.WriteLine();
            Console.WriteLine("Or use --synthetic flag for testing with generated data.");
            return 1;
        }

        // Print session info
        Console.WriteLine(session.ToString());

        // Find perfect trade (hindsight)
        Console.WriteLine("[*] Analyzing perfect hindsight trade...");
        var optimizer = new StrategyOptimizer(session);
        var (bestEntry, bestExit, maxProfit) = optimizer.FindPerfectTrade();
        
        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine("| PERFECT HINDSIGHT (theoretical maximum)          |");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine($"| Entry at:    ${bestEntry,8:F2}");
        Console.WriteLine($"| Exit at:     ${bestExit,8:F2}");
        Console.WriteLine($"| Max Profit:  ${maxProfit,8:F2} per share");
        Console.WriteLine($"| Max % Gain:  {(maxProfit / bestEntry * 100),8:F2}%");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();

        // Run optimization
        Console.WriteLine("[*] Running strategy optimization...");
        Console.WriteLine("    Testing parameter combinations...");
        Console.WriteLine();

        var config = OptimizationConfig.FromSession(session);
        var progress = new Progress<int>(p => 
        {
            Console.Write($"\r    Progress: [{new string('=', p / 5)}{new string(' ', 20 - p / 5)}] {p}%");
        });

        var results = optimizer.Optimize(config, progress);
        Console.WriteLine();
        Console.WriteLine();

        if (results.Count == 0)
        {
            Console.WriteLine("[!] No profitable strategies found.");
            Console.WriteLine("    The day may have been too choppy or range-bound.");
            return 0;
        }

        // Display results
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine($"| TOP {Math.Min(results.Count, 10)} STRATEGIES");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();

        foreach (var result in results.Take(10))
        {
            PrintResult(result);
        }

        // Generate IdiotScript for best strategy
        var best = results.First();
        string idiotScript = optimizer.GenerateIdiotScript(best);

        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine("| BEST STRATEGY - IDIOTSCRIPT                      |");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();
        Console.WriteLine(idiotScript);
        Console.WriteLine();

        // Run additional analysis if requested
        if (analysisMode == "full" || runMonteCarlo || runWalkForward)
        {
            await RunAdvancedAnalysis(session, results, runMonteCarlo, runWalkForward);
        }

        // Generate report
        if (analysisMode == "full" || analysisMode == "report")
        {
            Console.WriteLine("[*] Generating analysis report...");
            var reportGenerator = new ReportGenerator(session, results);
            string report = reportGenerator.GenerateTextReport();

            string reportFile = $"{symbol}-Report-{date:yyyyMMdd}.txt";
            await reportGenerator.SaveReportAsync(reportFile);
            Console.WriteLine($"[OK] Report saved to: {reportFile}");
        }

        // Learn from results if enabled
        if (learnFromResults && results.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("[*] Updating ticker profile with learned patterns...");

            var profileManager = new TickerProfileManager();
            var generator = new ProfileGenerator(profileManager);
            var profile = generator.GenerateFromOptimization(results, session);

            Console.WriteLine($"[OK] Profile updated: {profile.TotalTrades} total trades, " +
                $"{profile.Confidence * 100:F0}% confidence");

            if (profile.BestHours.Count > 0)
                Console.WriteLine($"    Best Hours: {string.Join(", ", profile.BestHours.Select(h => $"{h}:00"))}");
        }

        // Save to file if requested
        if (!string.IsNullOrEmpty(outputFile))
        {
            await File.WriteAllTextAsync(outputFile, idiotScript);
            Console.WriteLine($"[OK] Strategy saved to: {outputFile}");
        }
        else
        {
            // Offer to save
            Console.Write("Save strategy to file? (y/N): ");
            if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            {
                string defaultName = $"{symbol}-Optimized-{date:yyyyMMdd}.idiot";
                Console.Write($"Filename [{defaultName}]: ");
                string? filename = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(filename)) filename = defaultName;

                await File.WriteAllTextAsync(filename, idiotScript);
                Console.WriteLine($"[OK] Strategy saved to: {filename}");
            }
        }

        return 0;
    }

    private static async Task RunAdvancedAnalysis(
        BackTestSession session,
        List<OptimizationResult> results,
        bool runMonteCarlo,
        bool runWalkForward)
    {
        var best = results.FirstOrDefault();
        if (best == null) return;

        // Monte Carlo Analysis
        if (runMonteCarlo && best.SimulationResult.Trades.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("[*] Running Monte Carlo simulation...");

            var mcConfig = new MonteCarloConfig { Iterations = 1000 };
            var mcSimulator = new MonteCarloSimulator(mcConfig);

            var mcProgress = new Progress<int>(p =>
            {
                Console.Write($"\r    Monte Carlo: [{new string('=', p / 5)}{new string(' ', 20 - p / 5)}] {p}%");
            });

            var mcResult = mcSimulator.Simulate(best.SimulationResult.Trades, mcProgress);
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine(mcResult.ToString());
        }

        // Walk-Forward Analysis
        if (runWalkForward)
        {
            Console.WriteLine();
            Console.WriteLine("[*] Running walk-forward analysis...");

            var wfConfig = new WalkForwardConfig
            {
                TrainingWindowSize = 120,  // 2 hours
                TestingWindowSize = 60,    // 1 hour
                StepSize = 30              // 30 minutes
            };

            var wfAnalyzer = new WalkForwardAnalyzer(session, wfConfig);

            var wfProgress = new Progress<string>(msg =>
            {
                Console.WriteLine($"    {msg}");
            });

            var wfResult = wfAnalyzer.Analyze(wfProgress);
            Console.WriteLine();
            Console.WriteLine(wfResult.ToString());

            // Show interpretation
            if (wfResult.RobustnessScore >= 70)
                Console.WriteLine("[OK] Strategy appears ROBUST - good for live trading.");
            else if (wfResult.RobustnessScore >= 50)
                Console.WriteLine("[*]  Strategy shows MODERATE robustness - use with caution.");
            else
                Console.WriteLine("[!]  Strategy may be OVERFITTED - not recommended for live trading.");
        }

        await Task.CompletedTask;
    }

    private static void PrintResult(OptimizationResult result)
    {
        var p = result.Parameters;
        var s = result.SimulationResult;

        Console.WriteLine($"  #{result.Rank} | Score: {result.Score:F1}");
        Console.WriteLine($"     Entry: ${p.EntryPrice:F2} | TP: ${p.TakeProfitPrice:F2} (+{p.TakeProfitPercent:F1}%) | SL: ${p.StopLossPrice:F2} (-{p.StopLossPercent:F1}%)");
        Console.WriteLine($"     Trades: {s.Trades.Count} | Win Rate: {s.WinRate:F0}% | PnL: ${s.TotalPnL:F2} | PF: {s.ProfitFactor:F2}");
        
        if (p.TrailingStopPercent.HasValue)
            Console.Write($"     TSL: {p.TrailingStopPercent:F0}%");
        if (p.RequireAboveVwap)
            Console.Write(" | VWAP+");
        if (p.RequireHigherLows)
            Console.Write(" | HL+");
        
        Console.WriteLine();
        Console.WriteLine();
    }

    private static void PrintBanner()
    {
        Console.WriteLine();
        Console.WriteLine("+==================================================+");
        Console.WriteLine("|                                                  |");
        Console.WriteLine("|     IdiotProof BackTesting Engine                |");
        Console.WriteLine("|     Strategy Optimization & Training             |");
        Console.WriteLine("|                                                  |");
        Console.WriteLine("+==================================================+");
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  IdiotProof.BackTesting <ticker> <date> [options]");
        Console.WriteLine();
        Console.WriteLine("ARGUMENTS:");
        Console.WriteLine("  ticker     Stock symbol (e.g., AAPL, TSLA)");
        Console.WriteLine("  date       Date to backtest (yyyy-MM-dd)");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  --synthetic, -s        Use synthetic data for testing");
        Console.WriteLine("  --output, -o <file>    Output file for generated strategy");
        Console.WriteLine("  --analysis, -a <mode>  Analysis mode: 'full' or 'report'");
        Console.WriteLine("  --monte-carlo, -mc     Run Monte Carlo risk simulation");
        Console.WriteLine("  --walk-forward, -wf    Run walk-forward robustness analysis");
        Console.WriteLine();
        Console.WriteLine("LEARNING & PROFILES:");
        Console.WriteLine("  --learn, -l            Save results to ticker learning profile");
        Console.WriteLine("  --profile, -p          Show learned profile for a ticker");
        Console.WriteLine("  --show-metadata, -sm   Show historical metadata for a ticker");
        Console.WriteLine();
        Console.WriteLine("HISTORICAL METADATA:");
        Console.WriteLine("  --metadata, -meta      Analyze historical patterns (HOD/LOD, S/R levels)");
        Console.WriteLine("  --days, -d <count>     Number of days to analyze (default: 30)");
        Console.WriteLine();
        Console.WriteLine("AUTONOMOUS TRADING:");
        Console.WriteLine("  --autonomous, -auto    Run AutonomousTrading simulation");
        Console.WriteLine("  --mode, -m <mode>      Trading mode: conservative, balanced, aggressive");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  IdiotProof.BackTesting AAPL 2024-01-15");
        Console.WriteLine("  IdiotProof.BackTesting TSLA 2024-02-20 --synthetic");
        Console.WriteLine("  IdiotProof.BackTesting NVDA 2024-03-10 -o nvda.idiot");
        Console.WriteLine("  IdiotProof.BackTesting AAPL 2024-01-15 --analysis full");
        Console.WriteLine("  IdiotProof.BackTesting AAPL 2024-01-15 -mc -wf");
        Console.WriteLine();
        Console.WriteLine("METADATA EXAMPLES:");
        Console.WriteLine("  IdiotProof.BackTesting UUUU 2024-06-01 --metadata");
        Console.WriteLine("  IdiotProof.BackTesting AAPL 2024-06-01 -meta -d 60 --synthetic");
        Console.WriteLine("  IdiotProof.BackTesting TSLA 2024-06-01 --metadata --days 90");
        Console.WriteLine();
        Console.WriteLine("AUTONOMOUS EXAMPLES:");
        Console.WriteLine("  IdiotProof.BackTesting AAPL 2024-01-15 --autonomous --learn");
        Console.WriteLine("  IdiotProof.BackTesting TSLA 2024-02-20 -auto -m aggressive -l");
        Console.WriteLine("  IdiotProof.BackTesting NVDA 2024-03-10 -auto --synthetic");
        Console.WriteLine();
        Console.WriteLine("LEARNING EXAMPLES:");
        Console.WriteLine("  IdiotProof.BackTesting AAPL --profile       # Show learned profile");
        Console.WriteLine("  IdiotProof.BackTesting AAPL 2024-01-15 -l   # Learn from this day");
        Console.WriteLine();
        Console.WriteLine("DATA FORMAT:");
        Console.WriteLine("  Place CSV files in the 'Data' folder.");
        Console.WriteLine("  Naming: SYMBOL_yyyy-MM-dd.csv");
        Console.WriteLine("  Format: DateTime,Open,High,Low,Close,Volume");
        Console.WriteLine();
        Console.WriteLine("AUTONOMOUS MODES:");
        Console.WriteLine("  conservative - Higher thresholds (80/-80), fewer trades, safer");
        Console.WriteLine("  balanced     - Standard thresholds (70/-70), default mode");
        Console.WriteLine("  aggressive   - Lower thresholds (60/-60), more trades, higher risk");
        Console.WriteLine();
        Console.WriteLine("HISTORICAL METADATA:");
        Console.WriteLine("  Analyzes historical data to understand stock behavior:");
        Console.WriteLine("  - HOD/LOD timing patterns (when highs/lows typically occur)");
        Console.WriteLine("  - Support and resistance levels");
        Console.WriteLine("  - Optimal entry/exit points (with perfect hindsight)");
        Console.WriteLine("  - Gap behavior (fill rate, continuation rate)");
        Console.WriteLine("  - VWAP interaction patterns");
        Console.WriteLine("  Metadata is saved to: IdiotProof.Scripts\\Metadata\\");
        Console.WriteLine();
        Console.WriteLine("LEARNING SYSTEM:");
        Console.WriteLine("  When --learn is used, backtest results update the ticker's profile.");
        Console.WriteLine("  Profiles are stored in: IdiotProof.Scripts\\Profiles\\");
        Console.WriteLine("  Live AutonomousTrading uses these profiles to improve decisions.");
        Console.WriteLine();
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    /// <summary>
    /// Runs historical metadata analysis to understand stock behavior patterns.
    /// </summary>
    private static async Task<int> RunMetadataAnalysis(
        string symbol,
        DateOnly endDate,
        bool useSynthetic,
        int daysToAnalyze)
    {
        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine($"| HISTORICAL METADATA ANALYSIS: {symbol}");
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine();
        Console.WriteLine($"[*] Analyzing {daysToAnalyze} days ending {endDate:yyyy-MM-dd}");
        Console.WriteLine("[*] This will extract patterns about how the stock behaves:");
        Console.WriteLine("    - HOD/LOD timing patterns");
        Console.WriteLine("    - Support and resistance levels");
        Console.WriteLine("    - Optimal entry/exit points (with hindsight)");
        Console.WriteLine("    - Gap behavior patterns");
        Console.WriteLine("    - VWAP interaction patterns");
        Console.WriteLine();

        // Generate date list (going back from end date)
        var dates = new List<DateOnly>();
        var currentDate = endDate;
        for (int i = 0; i < daysToAnalyze; i++)
        {
            // Skip weekends
            if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
            {
                dates.Add(currentDate);
            }
            currentDate = currentDate.AddDays(-1);
        }
        dates.Reverse();  // Chronological order

        Console.WriteLine($"[*] Date range: {dates.First():yyyy-MM-dd} to {dates.Last():yyyy-MM-dd}");
        Console.WriteLine($"[*] Trading days to analyze: {dates.Count}");
        Console.WriteLine();

        // Load data provider
        IHistoricalDataProvider dataProvider = useSynthetic
            ? new SyntheticDataProvider()
            : new CsvHistoricalDataProvider(DataDirectory);

        // Progress reporting
        var progress = new Progress<string>(msg => Console.WriteLine($"    {msg}"));

        // Run analysis
        var analyzer = new MetadataAnalyzer();
        HistoricalMetadata metadata;

        try
        {
            metadata = await analyzer.AnalyzeMultipleDays(symbol, dataProvider, dates, progress);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERR] Analysis failed: {ex.Message}");
            if (!useSynthetic)
            {
                Console.WriteLine();
                Console.WriteLine("Tip: Use --synthetic flag for testing with generated data.");
            }
            return 1;
        }

        Console.WriteLine();

        // Generate and display report
        string report = analyzer.GenerateMetadataReport(metadata);
        Console.WriteLine(report);

        // Summary insights
        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------------+");
        Console.WriteLine("| KEY INSIGHTS                                     |");
        Console.WriteLine("+--------------------------------------------------+");

        // HOD/LOD insight
        if (metadata.DailyExtremes.HodInFirst30MinPercent > 40)
        {
            Console.WriteLine($"[*] HOD often occurs in first 30 min ({metadata.DailyExtremes.HodInFirst30MinPercent:F0}% of days)");
            Console.WriteLine("    -> Consider taking profits quickly on longs");
        }
        else if (metadata.DailyExtremes.HodInLast30MinPercent > 30)
        {
            Console.WriteLine($"[*] HOD often occurs near close ({metadata.DailyExtremes.HodInLast30MinPercent:F0}% of days)");
            Console.WriteLine("    -> Consider holding longs into close");
        }

        if (metadata.DailyExtremes.LodInFirst30MinPercent > 40)
        {
            Console.WriteLine($"[*] LOD often occurs in first 30 min ({metadata.DailyExtremes.LodInFirst30MinPercent:F0}% of days)");
            Console.WriteLine("    -> Consider waiting for pullback before buying");
        }

        // Gap insight
        if (metadata.GapBehavior.GapUpFillRate > 60)
        {
            Console.WriteLine($"[*] Gap ups fill {metadata.GapBehavior.GapUpFillRate:F0}% of the time");
            Console.WriteLine("    -> Consider fading gap ups or waiting for fill");
        }
        else if (metadata.GapBehavior.GapUpContinuationRate > 60)
        {
            Console.WriteLine($"[*] Gap ups continue higher {metadata.GapBehavior.GapUpContinuationRate:F0}% of the time");
            Console.WriteLine("    -> Consider buying gap ups for continuation");
        }

        // VWAP insight
        if (metadata.VwapBehavior.AvgPercentAboveVwap > 60)
        {
            Console.WriteLine($"[*] Spends {metadata.VwapBehavior.AvgPercentAboveVwap:F0}% of time above VWAP (bullish bias)");
            Console.WriteLine("    -> Favor long positions");
        }
        else if (metadata.VwapBehavior.AvgPercentAboveVwap < 40)
        {
            Console.WriteLine($"[*] Spends {100 - metadata.VwapBehavior.AvgPercentAboveVwap:F0}% of time below VWAP (bearish bias)");
            Console.WriteLine("    -> Favor short positions or wait for VWAP reclaim");
        }

        // Optimal trade points
        if (metadata.OptimalLongEntries.Count > 0)
        {
            var bestEntry = metadata.OptimalLongEntries.First();
            Console.WriteLine($"[*] Best long entries historically around {bestEntry.TimeOfDay:HH:mm}");
            Console.WriteLine($"    -> Win rate: {bestEntry.WinRate:F0}%, Avg profit: {bestEntry.PotentialProfitPercent:+0.0;-0.0}%");
        }

        Console.WriteLine();
        Console.WriteLine($"[OK] Metadata saved to: IdiotProof.Scripts/Metadata/{symbol}.metadata.json");
        Console.WriteLine("[*]  Future AutonomousTrading will use this data to make smarter decisions.");

        return 0;
    }
}
