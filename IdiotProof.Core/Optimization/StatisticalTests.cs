// ============================================================================
// Statistical Tests - Validate trading strategy significance
// ============================================================================
//
// Provides statistical tests to determine if a strategy's performance
// is statistically significant (not due to random chance).
//
// Tests included:
// - Binomial test: Is win rate significantly above 50%?
// - z-test: Is the win rate difference statistically significant?
// - Profit factor significance
// - Monte Carlo simulation: What's the probability of these results by chance?
//
// ============================================================================

namespace IdiotProof.BackTesting.Optimization;

/// <summary>
/// Results from statistical significance testing.
/// </summary>
public sealed record StatisticalTestResult
{
    /// <summary>Name of the test performed.</summary>
    public required string TestName { get; init; }

    /// <summary>The observed statistic (e.g., win rate, mean return).</summary>
    public double ObservedValue { get; init; }

    /// <summary>The expected value under null hypothesis.</summary>
    public double ExpectedValue { get; init; }

    /// <summary>p-value: probability of seeing this result if null hypothesis is true.</summary>
    public double PValue { get; init; }

    /// <summary>Standard error of the estimate.</summary>
    public double StandardError { get; init; }

    /// <summary>Z-score or test statistic.</summary>
    public double TestStatistic { get; init; }

    /// <summary>Number of observations (trades).</summary>
    public int SampleSize { get; init; }

    /// <summary>95% confidence interval lower bound.</summary>
    public double ConfidenceIntervalLow { get; init; }

    /// <summary>95% confidence interval upper bound.</summary>
    public double ConfidenceIntervalHigh { get; init; }

    /// <summary>Is the result statistically significant at 5% level?</summary>
    public bool IsSignificant => PValue < 0.05;

    /// <summary>Is the result statistically significant at 1% level?</summary>
    public bool IsHighlySignificant => PValue < 0.01;

    /// <summary>Significance level description.</summary>
    public string SignificanceDescription => PValue switch
    {
        < 0.001 => "Extremely Significant (p < 0.001)",
        < 0.01 => "Highly Significant (p < 0.01)",
        < 0.05 => "Significant (p < 0.05)",
        < 0.10 => "Marginally Significant (p < 0.10)",
        _ => "Not Significant"
    };

    public override string ToString() =>
        $"""
        +==================================================================+
        | {TestName,-40}                   |
        +==================================================================+
        | Observed:     {ObservedValue,10:F4}                              |
        | Expected:     {ExpectedValue,10:F4}                              |
        | Difference:   {ObservedValue - ExpectedValue,10:F4}                              |
        +------------------------------------------------------------------+
        | Test Stat:    {TestStatistic,10:F4}                              |
        | p-value:      {PValue,10:F6}                              |
        | Sample Size:  {SampleSize,10}                              |
        +------------------------------------------------------------------+
        | 95% CI:       [{ConfidenceIntervalLow:F4}, {ConfidenceIntervalHigh:F4}]                    |
        | Result:       {SignificanceDescription,-30}       |
        +==================================================================+
        """;
}

/// <summary>
/// Statistical tests for trading strategy validation.
/// </summary>
public static class StatisticalTests
{
    /// <summary>
    /// Tests if the observed win rate is significantly above a baseline (default 50%).
    /// Uses the one-sample binomial test / z-test for proportions.
    /// </summary>
    /// <param name="wins">Number of winning trades.</param>
    /// <param name="total">Total number of trades.</param>
    /// <param name="nullWinRate">Expected win rate under null hypothesis (default 0.5).</param>
    public static StatisticalTestResult WinRateTest(int wins, int total, double nullWinRate = 0.5)
    {
        if (total == 0)
        {
            return new StatisticalTestResult
            {
                TestName = "Win Rate Significance Test",
                ObservedValue = 0,
                ExpectedValue = nullWinRate,
                PValue = 1.0,
                SampleSize = 0
            };
        }

        double observedRate = (double)wins / total;
        double se = Math.Sqrt(nullWinRate * (1 - nullWinRate) / total);
        double zScore = (observedRate - nullWinRate) / se;

        // One-tailed p-value (testing if observed > expected)
        double pValue = 1 - NormalCDF(zScore);

        // 95% confidence interval for the observed rate
        double observedSe = Math.Sqrt(observedRate * (1 - observedRate) / total);
        double ciLow = observedRate - 1.96 * observedSe;
        double ciHigh = observedRate + 1.96 * observedSe;

        return new StatisticalTestResult
        {
            TestName = "Win Rate Significance Test",
            ObservedValue = observedRate,
            ExpectedValue = nullWinRate,
            PValue = pValue,
            StandardError = se,
            TestStatistic = zScore,
            SampleSize = total,
            ConfidenceIntervalLow = Math.Max(0, ciLow),
            ConfidenceIntervalHigh = Math.Min(1, ciHigh)
        };
    }

    /// <summary>
    /// Tests if the average return per trade is significantly different from zero.
    /// Uses one-sample t-test.
    /// </summary>
    /// <param name="returns">Array of trade returns (P&L percentages).</param>
    public static StatisticalTestResult ReturnSignificanceTest(double[] returns)
    {
        if (returns.Length == 0)
        {
            return new StatisticalTestResult
            {
                TestName = "Return Significance Test (t-test)",
                ObservedValue = 0,
                ExpectedValue = 0,
                PValue = 1.0,
                SampleSize = 0
            };
        }

        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1);
        double stdDev = Math.Sqrt(variance);
        double se = stdDev / Math.Sqrt(returns.Length);

        // T-statistic
        double tStat = se > 0 ? mean / se : 0;

        // Two-tailed p-value from t-distribution (approximated with normal for large n)
        double pValue = 2 * (1 - NormalCDF(Math.Abs(tStat)));

        // 95% CI
        double tCritical = 1.96; // Approximation for large n
        double ciLow = mean - tCritical * se;
        double ciHigh = mean + tCritical * se;

        return new StatisticalTestResult
        {
            TestName = "Return Significance Test (t-test)",
            ObservedValue = mean,
            ExpectedValue = 0,
            PValue = pValue,
            StandardError = se,
            TestStatistic = tStat,
            SampleSize = returns.Length,
            ConfidenceIntervalLow = ciLow,
            ConfidenceIntervalHigh = ciHigh
        };
    }

    /// <summary>
    /// Monte Carlo simulation to determine if results could be due to chance.
    /// Randomly shuffles trade outcomes and calculates probability of achieving
    /// observed results by chance.
    /// </summary>
    /// <param name="wins">Observed number of wins.</param>
    /// <param name="total">Total number of trades.</param>
    /// <param name="avgWinAmount">Average win amount.</param>
    /// <param name="avgLossAmount">Average loss amount (as positive number).</param>
    /// <param name="simulations">Number of Monte Carlo simulations.</param>
    public static StatisticalTestResult MonteCarloTest(
        int wins, int total, double avgWinAmount, double avgLossAmount, int simulations = 10000)
    {
        if (total == 0)
        {
            return new StatisticalTestResult
            {
                TestName = "Monte Carlo Simulation",
                ObservedValue = 0,
                ExpectedValue = 0,
                PValue = 1.0,
                SampleSize = 0
            };
        }

        var random = new Random(42); // Fixed seed for reproducibility
        double observedPnL = wins * avgWinAmount - (total - wins) * avgLossAmount;

        int betterByChance = 0;
        double sumRandomPnL = 0;
        var randomPnLs = new List<double>(simulations);

        for (int sim = 0; sim < simulations; sim++)
        {
            // Random coin flips with observed probabilities
            int randomWins = 0;
            for (int i = 0; i < total; i++)
            {
                if (random.NextDouble() < 0.5) randomWins++;
            }

            double randomPnL = randomWins * avgWinAmount - (total - randomWins) * avgLossAmount;
            randomPnLs.Add(randomPnL);
            sumRandomPnL += randomPnL;

            if (randomPnL >= observedPnL)
                betterByChance++;
        }

        double expectedPnL = sumRandomPnL / simulations;
        double pValue = (double)betterByChance / simulations;

        // Calculate confidence interval from simulation distribution
        randomPnLs.Sort();
        double ciLow = randomPnLs[(int)(simulations * 0.025)];
        double ciHigh = randomPnLs[(int)(simulations * 0.975)];

        return new StatisticalTestResult
        {
            TestName = "Monte Carlo Simulation",
            ObservedValue = observedPnL,
            ExpectedValue = expectedPnL,
            PValue = pValue,
            StandardError = 0,
            TestStatistic = (observedPnL - expectedPnL) / Math.Abs(expectedPnL + 0.001),
            SampleSize = simulations,
            ConfidenceIntervalLow = ciLow,
            ConfidenceIntervalHigh = ciHigh
        };
    }

    /// <summary>
    /// Compares two strategies using a z-test for proportions.
    /// Tests if strategy A has significantly higher win rate than strategy B.
    /// </summary>
    public static StatisticalTestResult CompareWinRates(
        int winsA, int totalA, int winsB, int totalB)
    {
        if (totalA == 0 || totalB == 0)
        {
            return new StatisticalTestResult
            {
                TestName = "Win Rate Comparison (z-test)",
                PValue = 1.0,
                SampleSize = 0
            };
        }

        double p1 = (double)winsA / totalA;
        double p2 = (double)winsB / totalB;

        // Pooled proportion
        double pPool = (double)(winsA + winsB) / (totalA + totalB);

        // Standard error for difference
        double se = Math.Sqrt(pPool * (1 - pPool) * (1.0 / totalA + 1.0 / totalB));

        // Z-score
        double zScore = se > 0 ? (p1 - p2) / se : 0;

        // Two-tailed p-value
        double pValue = 2 * (1 - NormalCDF(Math.Abs(zScore)));

        return new StatisticalTestResult
        {
            TestName = "Win Rate Comparison (z-test)",
            ObservedValue = p1,
            ExpectedValue = p2,
            PValue = pValue,
            StandardError = se,
            TestStatistic = zScore,
            SampleSize = totalA + totalB,
            ConfidenceIntervalLow = p1 - 1.96 * se,
            ConfidenceIntervalHigh = p1 + 1.96 * se
        };
    }

    /// <summary>
    /// Calculates the minimum sample size needed for statistical significance
    /// given desired win rate and power.
    /// </summary>
    /// <param name="targetWinRate">The win rate you want to detect (e.g., 0.60).</param>
    /// <param name="nullWinRate">The baseline win rate (default 0.50).</param>
    /// <param name="alpha">Significance level (default 0.05).</param>
    /// <param name="power">Statistical power (default 0.80).</param>
    public static int CalculateRequiredSampleSize(
        double targetWinRate, double nullWinRate = 0.5, double alpha = 0.05, double power = 0.80)
    {
        // z-values for significance and power
        double zAlpha = InverseNormalCDF(1 - alpha);
        double zBeta = InverseNormalCDF(power);

        double effect = targetWinRate - nullWinRate;
        double avgP = (targetWinRate + nullWinRate) / 2;

        // Sample size formula for proportion test
        double n = 2 * avgP * (1 - avgP) * Math.Pow(zAlpha + zBeta, 2) / (effect * effect);

        return (int)Math.Ceiling(n);
    }

    /// <summary>
    /// Calculates Sharpe-like ratio for trade returns.
    /// (Mean return / Std dev of returns) * sqrt(trades per year)
    /// </summary>
    public static double CalculateSharpeRatio(double[] returns, int tradesPerYear = 252)
    {
        if (returns.Length < 2) return 0;

        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1);
        double stdDev = Math.Sqrt(variance);

        if (stdDev == 0) return mean > 0 ? double.MaxValue : 0;

        return mean / stdDev * Math.Sqrt(tradesPerYear);
    }

    /// <summary>
    /// Calculates information ratio (risk-adjusted excess return).
    /// </summary>
    public static double CalculateInformationRatio(double[] returns, double[] benchmarkReturns)
    {
        if (returns.Length != benchmarkReturns.Length || returns.Length < 2)
            return 0;

        var excessReturns = returns.Zip(benchmarkReturns, (r, b) => r - b).ToArray();
        return CalculateSharpeRatio(excessReturns, 1);
    }

    /// <summary>
    /// Standard normal CDF (cumulative distribution function).
    /// </summary>
    private static double NormalCDF(double x)
    {
        // Approximation using error function
        double a1 = 0.254829592;
        double a2 = -0.284496736;
        double a3 = 1.421413741;
        double a4 = -1.453152027;
        double a5 = 1.061405429;
        double p = 0.3275911;

        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2);

        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return 0.5 * (1.0 + sign * y);
    }

    /// <summary>
    /// Inverse normal CDF (quantile function).
    /// Uses rational approximation.
    /// </summary>
    private static double InverseNormalCDF(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;

        // Coefficients for rational approximation
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02,
                      -2.759285104469687e+02, 1.383577518672690e+02,
                      -3.066479806614716e+01, 2.506628277459239e+00 };

        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02,
                      -1.556989798598866e+02, 6.680131188771972e+01,
                      -1.328068155288572e+01 };

        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01,
                      -2.400758277161838e+00, -2.549732539343734e+00,
                       4.374664141464968e+00, 2.938163982698783e+00 };

        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01,
                       2.445134137142996e+00, 3.754408661907416e+00 };

        double pLow = 0.02425;
        double pHigh = 1 - pLow;

        double q, r;

        if (p < pLow)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= pHigh)
        {
            q = p - 0.5;
            r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                   (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
    }
}

/// <summary>
/// Comprehensive strategy analysis with all statistical tests.
/// </summary>
public sealed class StrategyAnalysis
{
    public required string StrategyName { get; init; }
    public required int TotalTrades { get; init; }
    public required int WinningTrades { get; init; }
    public required double[] Returns { get; init; }
    public required double AvgWin { get; init; }
    public required double AvgLoss { get; init; }
    public required double TotalPnL { get; init; }

    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades : 0;
    public double ProfitFactor => AvgLoss > 0 ? (WinningTrades * AvgWin) / ((TotalTrades - WinningTrades) * AvgLoss) : 0;

    public StatisticalTestResult WinRateSignificance => 
        StatisticalTests.WinRateTest(WinningTrades, TotalTrades);

    public StatisticalTestResult ReturnSignificance => 
        StatisticalTests.ReturnSignificanceTest(Returns);

    public StatisticalTestResult MonteCarloSignificance => 
        StatisticalTests.MonteCarloTest(WinningTrades, TotalTrades, AvgWin, AvgLoss);

    public double SharpeRatio => StatisticalTests.CalculateSharpeRatio(Returns);

    public int MinTradesFor80PercentDetection => 
        StatisticalTests.CalculateRequiredSampleSize(0.80, 0.50, 0.05, 0.80);

    public bool IsStatisticallyBetter => 
        WinRateSignificance.IsSignificant && 
        ReturnSignificance.IsSignificant && 
        WinRateSignificance.ObservedValue > 0.5;

    public string GetFullReport()
    {
        return $"""
            +======================================================================+
            | STATISTICAL ANALYSIS: {StrategyName,-35}        |
            +======================================================================+
            
            PERFORMANCE SUMMARY
            +----------------------------------------------------------------------+
            | Total Trades:     {TotalTrades,8}                                     |
            | Win Rate:         {WinRate * 100,8:F2}%                                    |
            | Profit Factor:    {ProfitFactor,8:F2}                                     |
            | Total P&L:        ${TotalPnL,10:N2}                                   |
            | Sharpe Ratio:     {SharpeRatio,8:F2}                                     |
            +----------------------------------------------------------------------+
            
            {WinRateSignificance}
            
            {ReturnSignificance}
            
            {MonteCarloSignificance}
            
            CONCLUSION
            +----------------------------------------------------------------------+
            | Statistically Valid: {(IsStatisticallyBetter ? "YES" : "NO"),-10}                              |
            | Min trades for 80% win rate detection: {MinTradesFor80PercentDetection,5}                      |
            +----------------------------------------------------------------------+
            """;
    }
}
