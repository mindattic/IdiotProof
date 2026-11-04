namespace IdiotProof.Evidence.EventStudy;

/// <summary>
/// The market model R_i = α + β·R_m + ε fitted by OLS over an estimation window. Abnormal return =
/// what the stock did beyond what the market alone explains; its prediction-error variance widens for
/// market days unlike the estimation sample, which Patell's standardization needs.
/// </summary>
public sealed record MarketModel(double Alpha, double Beta, double ResidualVariance, int Observations, double MarketMean, double MarketSumSquares)
{
    public static MarketModel? Fit(IReadOnlyList<double> stock, IReadOnlyList<double> market)
    {
        if (stock.Count != market.Count) throw new ArgumentException("Length mismatch.");
        int n = stock.Count;
        if (n < 3) return null;
        double mx = 0, my = 0;
        for (int i = 0; i < n; i++) { mx += market[i]; my += stock[i]; }
        mx /= n; my /= n;
        double sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++) { double dx = market[i] - mx; sxx += dx * dx; sxy += dx * (stock[i] - my); }
        double beta = sxx > 0 ? sxy / sxx : 0;
        double alpha = my - beta * mx;
        double sse = 0;
        for (int i = 0; i < n; i++) { double e = stock[i] - alpha - beta * market[i]; sse += e * e; }
        return new MarketModel(alpha, beta, sse / (n - 2), n, mx, sxx);
    }

    public double AbnormalReturn(double stockReturn, double marketReturn) => stockReturn - Alpha - Beta * marketReturn;

    /// <summary>Out-of-sample forecast-error variance s²·(1 + 1/L + (R_m − mean)² / Σ(R_m − mean)²).</summary>
    public double PredictionVariance(double marketReturn)
    {
        double lever = MarketSumSquares > 0 ? (marketReturn - MarketMean) * (marketReturn - MarketMean) / MarketSumSquares : 0;
        return ResidualVariance * (1 + 1.0 / Observations + lever);
    }
}
