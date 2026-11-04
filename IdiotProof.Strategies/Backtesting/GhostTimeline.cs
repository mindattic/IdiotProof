using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Strategies.Backtesting;

/// <summary>One condition's pass/fail on a bar.</summary>
public sealed record GhostCondition(string Label, bool Passed);

/// <summary>An entry, exit or trigger that happened on a bar.</summary>
public sealed record GhostEvent(string Kind, decimal Price, string Text);

/// <summary>Everything the ghost shows at one bar of the replay.</summary>
public sealed record GhostFrame(
    int Index,
    DateTime Utc,
    decimal Open, decimal High, decimal Low, decimal Close,
    IReadOnlyList<int> BranchTaken,
    IReadOnlyList<GhostCondition> Conditions,
    bool AllSatisfied,
    int PositionShares,
    TradeDirection? PositionDirection,
    decimal? ActiveStop,
    IReadOnlyList<GhostEvent> Events);

/// <summary>One branch at a fork: its condition, whether it held, whether it was taken, and its price levels from the fork bar.</summary>
public sealed record GhostForkBranch(
    int Branch, string Label, string? Condition, bool Passed, bool Taken, decimal? Stop, decimal? Target);

/// <summary>A bar where an If() block resolved to a different branch than the bar before (or its first resolution).</summary>
public sealed record GhostFork(
    int Index, DateTime Utc, int Block, int FromBranch, int ToBranch,
    IReadOnlyList<GhostForkBranch> Branches,
    IReadOnlyDictionary<string, double> Indicators);

/// <summary>
/// A stretch of bars where one If() block held one branch (or, when <see cref="IsChop"/>, kept flipping
/// between branches in runs shorter than the minimum). Ends are inclusive frame indices.
/// </summary>
public sealed record GhostSegment(
    int Block, int StartIndex, int EndIndex, int Branch, bool IsChop, int Flips,
    IReadOnlyDictionary<int, int> BarsByBranch)
{
    public int Bars => EndIndex - StartIndex + 1;
}

/// <summary>Collapses a per-bar branch sequence into readable segments.</summary>
public static class GhostSegmentation
{
    /// <summary>
    /// Runs of at least <paramref name="minRunLength"/> bars become their own segment; consecutive
    /// shorter runs merge into one chop segment (or a plain segment when they all took the same branch).
    /// Flips counts branch changes inside a segment; a change at a segment boundary is not counted.
    /// </summary>
    public static IReadOnlyList<GhostSegment> Segment(int block, IReadOnlyList<int> branchPerBar, int minRunLength)
    {
        if (minRunLength < 1) throw new ArgumentOutOfRangeException(nameof(minRunLength));
        var runs = new List<(int Start, int End, int Branch)>();
        for (int i = 0; i < branchPerBar.Count; i++)
        {
            if (runs.Count > 0 && runs[^1].Branch == branchPerBar[i]) runs[^1] = (runs[^1].Start, i, runs[^1].Branch);
            else runs.Add((i, i, branchPerBar[i]));
        }

        var segments = new List<GhostSegment>();
        var pending = new List<(int Start, int End, int Branch)>();
        void Flush()
        {
            if (pending.Count == 0) return;
            segments.Add(Build(block, pending, branchPerBar));
            pending.Clear();
        }
        foreach (var run in runs)
        {
            if (run.End - run.Start + 1 >= minRunLength)
            {
                Flush();
                pending.Add(run);
                Flush();
            }
            else pending.Add(run);
        }
        Flush();
        return segments;
    }

    private static GhostSegment Build(int block, List<(int Start, int End, int Branch)> runs, IReadOnlyList<int> seq)
    {
        var bars = new Dictionary<int, int>();
        foreach (var r in runs) bars[r.Branch] = bars.GetValueOrDefault(r.Branch) + r.End - r.Start + 1;
        bool chop = bars.Count > 1;
        return new GhostSegment(block, runs[0].Start, runs[^1].End, chop ? -1 : runs[0].Branch, chop, runs.Count - 1, bars);
    }
}

/// <summary>A branch in the exported tree, with how often the replay took it and how many trades it entered.</summary>
public sealed record BranchTreeBranch(int Branch, string Label, string? Condition, string Overrides, int BarsTaken, int TradesEntered);

public sealed record BranchTreeBlock(int Block, IReadOnlyList<BranchTreeBranch> Branches);

/// <summary>The strategy's decision tree with the replay's counts on it; round-trips through JSON.</summary>
public sealed record BranchTree(string Symbol, string Strategy, DateOnly Date, IReadOnlyList<string> BaseConditions, IReadOnlyList<BranchTreeBlock> Blocks)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BranchTree FromJson(string json) =>
        JsonSerializer.Deserialize<BranchTree>(json, Json) ?? throw new JsonException("Empty branch tree.");

    /// <summary>A Mermaid flowchart of the tree, so the export renders as a diagram anywhere Mermaid does.</summary>
    public string ToMermaid()
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine($"  start[\"{Esc(Symbol)} · {Esc(Strategy)} · {Date:yyyy-MM-dd}\"]");
        string prev = "start";
        if (BaseConditions.Count > 0)
        {
            sb.AppendLine($"  base[\"{Esc(string.Join(" AND ", BaseConditions))}\"]");
            sb.AppendLine("  start --> base");
            prev = "base";
        }
        foreach (var block in Blocks)
        {
            var node = $"b{block.Block}";
            sb.AppendLine($"  {node}{{\"If block {block.Block + 1}\"}}");
            sb.AppendLine($"  {prev} --> {node}");
            foreach (var br in block.Branches)
            {
                var leaf = $"{node}_{br.Branch}";
                sb.AppendLine($"  {leaf}[\"{Esc(br.Label)}: {Esc(br.Overrides)}<br/>{br.BarsTaken} bars · {br.TradesEntered} trades\"]");
                var edge = br.BarsTaken > 0 ? "-->" : "-.->";
                sb.AppendLine($"  {node} {edge}|\"{Esc(br.Condition ?? "otherwise")}\"| {leaf}");
            }
            prev = node;
        }
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\"", "'");
}

/// <summary>
/// The replay as a sequence of frames the ghost panel steps through: per bar, which branch each
/// If() block took, every entry condition's result, the position and its stop, and what happened.
/// Built from a <see cref="BacktestReport"/>, so the ghost shows exactly what the backtester (and the
/// Monitor, which resolves branches the same way) would have done.
/// </summary>
public sealed class GhostTimeline
{
    public string Symbol { get; }
    public string Strategy { get; }
    public DateOnly Date { get; }
    public IReadOnlyList<GhostFrame> Frames { get; }
    public IReadOnlyList<GhostFork> Forks { get; }
    public BranchTree Tree { get; }
    public BacktestReport Report { get; }

    private GhostTimeline(string symbol, string strategy, DateOnly date, IReadOnlyList<GhostFrame> frames,
        IReadOnlyList<GhostFork> forks, BranchTree tree, BacktestReport report)
    {
        Symbol = symbol; Strategy = strategy; Date = date; Frames = frames; Forks = forks; Tree = tree; Report = report;
    }

    /// <summary>Runs the backtester and builds the timeline in one step.</summary>
    public static GhostTimeline Run(StrategyDefinition definition, IReadOnlyList<Candle> candles, BacktestOptions? options = null)
        => From(definition, StrategyBacktester.Run(definition, candles, options));

    public static GhostTimeline From(StrategyDefinition definition, BacktestReport report)
    {
        var rows = report.ConditionTable;
        var frames = new List<GhostFrame>(rows.Count);
        var forks = new List<GhostFork>();
        var takenCounts = definition.ConditionalBlocks.Select(b => new int[b.Branches.Count]).ToArray();
        var tradeCounts = definition.ConditionalBlocks.Select(b => new int[b.Branches.Count]).ToArray();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var (shares, dir, stop) = PositionAfter(report.Trades, row.Utc);
            var events = EventsAt(report, row.Utc);

            frames.Add(new GhostFrame(i, row.Utc, row.Open, row.High, row.Low, row.Close, row.BranchTaken,
                row.Labels.Select((l, k) => new GhostCondition(l, row.Results[k])).ToList(),
                row.AllSatisfied, shares, dir, stop, events));

            for (int b = 0; b < row.BranchTaken.Count && b < definition.ConditionalBlocks.Count; b++)
            {
                int now = row.BranchTaken[b];
                if (now >= 0)
                {
                    takenCounts[b][now]++;
                    if (row.OpenedTrade) tradeCounts[b][now]++;
                }
                int before = i == 0 ? -2 : (b < rows[i - 1].BranchTaken.Count ? rows[i - 1].BranchTaken[b] : -1);
                if (before == now) continue;
                var block = definition.ConditionalBlocks[b];
                var results = row.BranchConditionResults[b];
                var branches = block.Branches.Select((br, k) =>
                {
                    var (s, t) = Levels(definition, br.Overrides, row.Close);
                    return new GhostForkBranch(k, BranchLabel(block, k), br.Condition?.ToScript(), results[k], k == now, s, t);
                }).ToList();
                forks.Add(new GhostFork(i, row.Utc, b, before == -2 ? -1 : before, now, branches, row.Indicators));
            }
        }

        var tree = new BranchTree(
            report.Symbol, report.StrategyName, report.Date,
            definition.EntryConditions.Select(c => c.ToScript()).ToList(),
            definition.ConditionalBlocks.Select((block, b) => new BranchTreeBlock(b,
                block.Branches.Select((br, k) => new BranchTreeBranch(k, BranchLabel(block, k), br.Condition?.ToScript(),
                    br.Overrides.ToScript(), takenCounts[b][k], tradeCounts[b][k])).ToList())).ToList());

        return new GhostTimeline(report.Symbol, report.StrategyName, report.Date, frames, forks, tree, report);
    }

    /// <summary>Every If() block's branch history collapsed into segments (see <see cref="GhostSegmentation"/>).</summary>
    public IReadOnlyList<GhostSegment> Segments(int minRunLength)
    {
        var all = new List<GhostSegment>();
        int blocks = Tree.Blocks.Count;
        for (int b = 0; b < blocks; b++)
        {
            var seq = Frames.Select(f => b < f.BranchTaken.Count ? f.BranchTaken[b] : -1).ToList();
            all.AddRange(GhostSegmentation.Segment(b, seq, minRunLength));
        }
        return all;
    }

    public static string BranchLabel(ConditionalBlock block, int k) =>
        block.Branches[k].Condition is null ? "Else" : k == 0 ? "If" : $"ElseIf {k}";

    /// <summary>The stop and first target a branch would use if it entered at <paramref name="entry"/>; base values fill gaps.</summary>
    public static (decimal? Stop, decimal? Target) Levels(StrategyDefinition def, StrategyOverrides o, decimal entry)
    {
        bool isLong = (o.Direction ?? def.Direction) != TradeDirection.Short;
        decimal? stop = null;
        double? stopPrice = o.StopLossPrice ?? (o.StopLossPercent is null ? def.StopLossPrice : null);
        double? stopPct = o.StopLossPercent ?? (o.StopLossPrice is null ? def.StopLossPercent : null);
        if (stopPrice is { } sp) stop = (decimal)sp;
        else if (stopPct is { } pct) stop = isLong ? entry * (1m - (decimal)(pct / 100.0)) : entry * (1m + (decimal)(pct / 100.0));

        decimal? target = null;
        IReadOnlyList<TakeProfitTarget> targets = o.TakeProfitTargets.Count > 0
            ? o.TakeProfitTargets
            : o.TakeProfitPrice is null ? def.TakeProfitTargets : new List<TakeProfitTarget>();
        if (targets.Count > 0)
            target = (decimal)(isLong ? targets.Min(t => t.Price) : targets.Max(t => t.Price));
        else if ((o.TakeProfitPrice ?? def.TakeProfitPrice) is { } tp)
            target = (decimal)tp;
        return (stop, target);
    }

    /// <summary>Shares held once this bar's fills are applied (entries on or before the bar, exits on or before it).</summary>
    private static (int Shares, TradeDirection? Dir, decimal? Stop) PositionAfter(IReadOnlyList<BacktestTrade> trades, DateTime utc)
    {
        foreach (var t in trades)
        {
            if (t.EntryUtc > utc) continue;
            int left = t.Quantity - t.Exits.Where(f => f.Utc <= utc).Sum(f => f.Shares);
            if (left > 0) return (left, t.Direction, t.StopPrice > 0m ? t.StopPrice : null);
        }
        return (0, null, null);
    }

    private static IReadOnlyList<GhostEvent> EventsAt(BacktestReport report, DateTime utc)
    {
        var list = new List<GhostEvent>();
        foreach (var t in report.Triggers.Where(t => t.Utc == utc))
            list.Add(new GhostEvent("trigger", t.Price, t.Condition));
        foreach (var t in report.Trades)
        {
            if (t.EntryUtc == utc)
                list.Add(new GhostEvent("entry", t.EntryPrice, $"{(t.Direction == TradeDirection.Short ? "Short" : "Buy")} {t.Quantity} @ {t.EntryPrice:0.00##}"));
            foreach (var f in t.Exits.Where(f => f.Utc == utc))
                list.Add(new GhostEvent("exit", f.Price, $"Exit {f.Shares} @ {f.Price:0.00##} · {f.Reason}"));
        }
        return list;
    }
}

/// <summary>Side-by-side summary of several ghost runs (same strategy on other days/tickers, or other strategies on one day).</summary>
public sealed record GhostRunSummary(string Symbol, string Strategy, DateOnly Date, int Bars, int Forks, int Trades, decimal PnL, decimal WinRate,
    IReadOnlyList<string> BranchesUsed);

public static class GhostComparison
{
    public static IReadOnlyList<GhostRunSummary> Summarize(IEnumerable<GhostTimeline> runs) =>
        runs.Select(r => new GhostRunSummary(r.Symbol, r.Strategy, r.Date, r.Frames.Count, r.Forks.Count,
            r.Report.TradeCount, r.Report.TotalPnL, r.Report.WinRate,
            r.Tree.Blocks.SelectMany(b => b.Branches.Where(x => x.BarsTaken > 0).Select(x => $"#{b.Block + 1} {x.Label}")).ToList()))
            .ToList();
}
