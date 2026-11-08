using System.Text;

namespace IdiotProof.Core.Research.Gappers.Explore;

public sealed partial class GapperExploreStage
{
    public static readonly int[] CorobsKs = [25, 50, 100, 200];

    /// <summary>Feature subsets for the Corobs grid, as unions of ablation blocks (null = every feature).</summary>
    public static readonly (string Name, string[]? Blocks)[] CorobsSubsets =
    [
        ("all features", null),
        ("volume + velocity", ["volume vs time of day", "velocity"]),
        ("volume + velocity + catalyst", ["volume vs time of day", "velocity", "catalyst"]),
        ("volume + velocity + prior days + sympathy", ["volume vs time of day", "velocity", "prior days", "sympathy"]),
    ];

    /// <summary>
    /// Track 3: the Corobs look-alike learner over neighbour counts and feature subsets on mornings still
    /// forming at t, against the boosted trees on the same rows; one ledger trial per cell.
    /// </summary>
    private (List<string>, int, string?) Corobs(IReadOnlyList<string> times)
    {
        var decision = times.Count == 1 ? times[0] : "08:00";
        var t = Load(decision);
        var folds = Folds(t);
        var rows = Scorable(t, formingOnly: true);
        var all = AllFeatures(t);
        var md = new StringBuilder(Header($"Corobs look-alike variations at {decision} (mornings still below +20%)", folds));
        var trials = new List<ExploreTrial>();
        var json = new List<object>();
        var boosted = ScoreFolds(t, rows, folds, (fit, eval) => Boosted(t, all, fit, eval));
        md.AppendLine($"Reference: boosted trees, all features, pooled AUC {F(boosted.Pooled)} ({string.Join(" / ", boosted.Folds.Select(F))}).");
        md.AppendLine();
        md.AppendLine("| subset | " + string.Join(" | ", CorobsKs.Select(k => $"k={k}")) + " |");
        md.AppendLine("|---|" + string.Concat(CorobsKs.Select(_ => "---|")));
        foreach (var (name, blocks) in CorobsSubsets)
        {
            var idx = blocks is null ? all : all.Where(f => blocks.SelectMany(b => Blocks[b]).Any(pre => t.FeatureNames[f].StartsWith(pre, StringComparison.Ordinal))).ToArray();
            if (idx.Length == 0) continue;
            var cells = new List<string>();
            foreach (var k in CorobsKs)
            {
                var r = ScoreFolds(t, rows, folds, (fit, eval) => CorobsLearner(t, idx, fit, eval, k));
                cells.Add($"{F(r.Pooled)} ({string.Join("/", r.Folds.Select(F))})");
                trials.Add(new ExploreTrial { Track = "corobs", Config = $"t={decision} k={k} subset={name} features={idx.Length}", Metric = "weightedAuc", FoldValues = r.Folds, Pooled = r.Pooled, Rows = r.Rows });
                json.Add(new { subset = name, k, features = idx.Length, r.Folds, r.Pooled, r.Rows });
            }
            md.AppendLine($"| {name} ({idx.Length}) | {string.Join(" | ", cells)} |");
        }
        md.AppendLine();
        md.AppendLine("Pooled weighted AUC with the four fold values in brackets. The boosted reference is logged by `explore models`, not here.");
        return Write("corobs", md, trials, json);
    }
}
