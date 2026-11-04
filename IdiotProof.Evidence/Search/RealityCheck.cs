namespace IdiotProof.Evidence.Search;

/// <summary>Data-snooping test result: is the best config better than zero once the search itself is accounted for?</summary>
public sealed record RealityCheckResult(int Configs, int Periods, int BestConfig, double BestMean,
    double WhitePValue, double SpaPValue, int Resamples, double MeanBlockLength);

/// <summary>
/// White's (2000) reality check and Hansen's (2005) consistent SPA test with Politis-Romano stationary
/// bootstrap. Input: one aligned return series per config (period t is the same day/symbol for every
/// config, 0 when it didn't trade). The bootstrap resamples whole periods with random block lengths, so the
/// same-day correlation between configs and any serial dependence survive the resampling.
/// </summary>
public static class RealityCheck
{
    public static RealityCheckResult Run(IReadOnlyList<double[]> configReturns, int resamples = 1000,
        double meanBlockLength = 5, int seed = 1337)
    {
        int k = configReturns.Count;
        if (k == 0) throw new ArgumentException("No configs.", nameof(configReturns));
        int n = configReturns[0].Length;
        if (configReturns.Any(r => r.Length != n)) throw new ArgumentException("Series must be aligned to the same periods.");
        if (n < 2) return new RealityCheckResult(k, n, 0, 0, 1, 1, 0, meanBlockLength);
        if (meanBlockLength < 1) throw new ArgumentOutOfRangeException(nameof(meanBlockLength));

        var means = new double[k];
        for (int j = 0; j < k; j++) means[j] = EvidenceStats.Mean(configReturns[j]);
        int best = 0;
        for (int j = 1; j < k; j++) if (means[j] > means[best]) best = j;

        double sqrtN = Math.Sqrt(n);
        var rng = new Random(seed);
        double p = 1.0 / meanBlockLength;
        var boot = new double[resamples, k];
        var idx = new int[n];
        for (int b = 0; b < resamples; b++)
        {
            int t = rng.Next(n);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) t = rng.NextDouble() < p ? rng.Next(n) : (t + 1) % n;
                idx[i] = t;
            }
            for (int j = 0; j < k; j++)
            {
                var r = configReturns[j];
                double s = 0;
                for (int i = 0; i < n; i++) s += r[idx[i]];
                boot[b, j] = s / n;
            }
        }

        // Per-config scale for the studentized SPA statistic, from the bootstrap spread of sqrt(n)·mean.
        var omega = new double[k];
        for (int j = 0; j < k; j++)
        {
            double sum = 0, sumSq = 0;
            for (int b = 0; b < resamples; b++)
            {
                double z = sqrtN * (boot[b, j] - means[j]);
                sum += z; sumSq += z * z;
            }
            double mu = sum / resamples;
            omega[j] = Math.Sqrt(Math.Max(1e-24, sumSq / resamples - mu * mu));
        }

        double whiteStat = double.NegativeInfinity, spaStat = 0;
        for (int j = 0; j < k; j++)
        {
            whiteStat = Math.Max(whiteStat, sqrtN * means[j]);
            spaStat = Math.Max(spaStat, sqrtN * means[j] / omega[j]);
        }

        // SPA_c recentering: configs that are clearly bad don't inflate the null distribution.
        double threshold = -Math.Sqrt(2 * Math.Log(Math.Log(Math.Max(n, 3))));
        var centered = new double[k];
        for (int j = 0; j < k; j++)
            centered[j] = sqrtN * means[j] / omega[j] >= threshold ? means[j] : 0;

        int whiteExceed = 0, spaExceed = 0;
        for (int b = 0; b < resamples; b++)
        {
            double vWhite = double.NegativeInfinity, vSpa = 0;
            for (int j = 0; j < k; j++)
            {
                vWhite = Math.Max(vWhite, sqrtN * (boot[b, j] - means[j]));
                vSpa = Math.Max(vSpa, sqrtN * (boot[b, j] - centered[j]) / omega[j]);
            }
            if (vWhite >= whiteStat) whiteExceed++;
            if (vSpa >= spaStat) spaExceed++;
        }

        return new RealityCheckResult(k, n, best, means[best],
            (double)whiteExceed / resamples, (double)spaExceed / resamples, resamples, meanBlockLength);
    }
}
