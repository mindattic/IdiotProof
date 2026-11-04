using IdiotProof.Evidence.Evaluation;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Evidence;

/// <summary>Renders a <see cref="HoldoutReport"/> the same way in every evidence operation.</summary>
internal static class BucketReportView
{
    public static TextTable Table(HoldoutReport report, int? width)
    {
        var table = new TextTable { MaxWidth = width }
            .Column("feature").Column("bucket", Align.Right).Column("train up", Align.Right).Column("n", Align.Right)
            .Column("test up", Align.Right).Column("95% CI", Align.Right).Column("n", Align.Right).Column("days", Align.Right)
            .Column("lift", Align.Right).Column("p", Align.Right).Column("q", Align.Right).Column("verdict");
        foreach (var b in report.AllBuckets)
        {
            bool none = b.HoldoutSamples == 0;
            table.Row(b.Feature, b.Bin.ToString(),
                b.Train.Samples == 0 ? "—" : EvidenceSupport.Rate((double)b.Train.Successes / b.Train.Samples), b.Train.Samples.ToString(),
                none ? "—" : EvidenceSupport.Rate(b.HoldoutRate),
                none ? "—" : $"{b.HoldoutLow * 100:0.0}–{b.HoldoutHigh * 100:0.0}%",
                b.HoldoutSamples.ToString(), b.Days.ToString(),
                double.IsNaN(b.Lift) ? "—" : b.Lift.ToString("0.00"),
                none ? "—" : b.P.ToString("0.000"), none ? "—" : b.Q.ToString("0.000"), b.Label);
        }
        return table;
    }

    public static object Data(HoldoutReport report) => report.Features.Select(f => new
    {
        Metric = f.Feature, f.Bins, TrainBaseRate = f.TrainBaseRate, TestBaseRate = f.HoldoutBaseRate,
        TrainSamples = f.TrainSamples, TestSamples = f.HoldoutSamples,
        Buckets = f.Buckets.Select(b => new
        {
            b.Bin, TrainUp = b.Train.Successes, TrainN = b.Train.Samples,
            TestUp = b.HoldoutUp, TestN = b.HoldoutSamples, TestLow = b.HoldoutLow, TestHigh = b.HoldoutHigh,
            b.Days, b.Lift, b.BinomialP, b.DayClusteredP, b.P, b.Q, Verdict = b.Label,
        }),
    }).ToList();

    public static string Footnote(HoldoutReport report) =>
        "Bucket edges come from training sessions only. p is the larger of an exact two-sided binomial test and a day-clustered test " +
        $"(bootstrap over days + t-test on daily rates) against the held-out base rate; q is Benjamini-Hochberg across all {report.Tests} buckets shown, " +
        $"and only q < {report.Alpha:0.##} is called significant.";
}
