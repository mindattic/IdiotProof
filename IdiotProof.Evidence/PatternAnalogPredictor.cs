using IdiotProof.Models;

namespace IdiotProof.Evidence;

/// <summary>What the historical look-alikes of the current window did next.</summary>
public sealed record AnalogVote(int Neighbors, int Up, int Down, double MeanForwardReturn, double MeanSimilarity,
    Direction Call, double UpProbability, (double Low, double High) UpInterval);

/// <summary>
/// "Has this shape happened before, and what happened after?" Indexes every past window with its
/// realized forward return, then calls a direction only when enough close look-alikes agree. The
/// probability it reports is the neighbors' own empirical up-rate (Laplace-smoothed), with a Wilson
/// interval, never a tuned weight.
/// </summary>
public sealed class PatternAnalogPredictor
{
    private readonly LshIndex<double> index;
    public int WindowLength { get; }
    public int Horizon { get; }
    public bool IncludeVolume { get; }

    public PatternAnalogPredictor(int windowLength, int horizon, bool includeVolume = false, int bands = 16, int bitsPerBand = 8, int seed = 1337)
    {
        WindowLength = windowLength;
        Horizon = horizon;
        IncludeVolume = includeVolume;
        index = new LshIndex<double>(includeVolume ? windowLength * 2 : windowLength, bands, bitsPerBand, seed);
    }

    public int Count => index.Count;

    /// <summary>
    /// Indexes windows ending at bars [from, to) whose forward label lies entirely before <paramref name="labelLimit"/>,
    /// so training never contains an outcome that resolves inside the test period.
    /// </summary>
    public void Train(IReadOnlyList<Candle> candles, int from, int to, int labelLimit)
    {
        for (int end = Math.Max(from, WindowLength - 1); end < to; end++)
        {
            if (end + Horizon >= labelLimit || end + Horizon >= candles.Count) break;
            double entry = (double)candles[end].Close;
            if (entry <= 0) continue;
            double fwd = (double)candles[end + Horizon].Close / entry - 1;
            index.Add(ShapeVector.FromWindow(candles, end, WindowLength, IncludeVolume), fwd);
        }
    }

    public AnalogVote Predict(IReadOnlyList<Candle> candles, int end, int k = 25, double minSimilarity = 0.8,
        int minNeighbors = 8, double minAgreement = 0.6, double deadBand = 0)
    {
        var shape = ShapeVector.FromWindow(candles, end, WindowLength, IncludeVolume);
        var neighbors = index.Query(shape, k, minSimilarity);
        int up = 0, down = 0;
        double sumRet = 0, sumSim = 0;
        foreach (var n in neighbors)
        {
            if (n.Payload > deadBand) up++;
            else if (n.Payload < -deadBand) down++;
            sumRet += n.Payload;
            sumSim += n.Similarity;
        }
        int count = neighbors.Count;
        int decided = up + down;
        double pUp = (up + 1.0) / (decided + 2.0);
        var ci = EvidenceStats.WilsonInterval(up, decided);

        var call = Direction.Flat;
        if (count >= minNeighbors && decided > 0)
        {
            if (pUp >= minAgreement) call = Direction.Up;
            else if (1 - pUp >= minAgreement) call = Direction.Down;
        }
        return new AnalogVote(count, up, down, count == 0 ? 0 : sumRet / count, count == 0 ? 0 : sumSim / count,
            call, pUp, ci);
    }
}
