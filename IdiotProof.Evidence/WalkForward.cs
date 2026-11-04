namespace IdiotProof.Evidence;

/// <summary>One train/test split; ends are exclusive. The test window starts Embargo bars after training ends.</summary>
public readonly record struct Fold(int TrainStart, int TrainEnd, int TestStart, int TestEnd);

/// <summary>
/// Walk-forward splits: the model only ever sees the past. An embargo gap at least as long as the
/// label horizon keeps a training label (which looks h bars ahead) from peeking into the test window.
/// </summary>
public static class WalkForward
{
    public static IReadOnlyList<Fold> Folds(int count, int minTrain, int testSize, int embargo, bool expanding = true)
    {
        if (minTrain < 1 || testSize < 1 || embargo < 0) throw new ArgumentOutOfRangeException(nameof(minTrain));
        var folds = new List<Fold>();
        int trainEnd = minTrain;
        while (true)
        {
            int testStart = trainEnd + embargo;
            int testEnd = Math.Min(count, testStart + testSize);
            if (testStart >= count || testEnd <= testStart) break;
            int trainStart = expanding ? 0 : trainEnd - minTrain;
            folds.Add(new Fold(trainStart, trainEnd, testStart, testEnd));
            trainEnd += testSize;
        }
        return folds;
    }
}
