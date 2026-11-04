using System.Globalization;
using System.Text.RegularExpressions;
using IdiotProof.Core.Evidence;
using IdiotProof.Core.Services;
using IdiotProof.Evidence;
using IdiotProof.Evidence.Search;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;
using IdiotProof.Strategies.Backtesting;

namespace IdiotProof.Operations.Evidence;

public sealed record EvidenceSearchArgs
{
    [Argument(0, Name = "symbols", Description = "Tickers to score every config on, e.g. SNDL MULN.")]
    public string[] Symbols { get; init; } = [];

    [Option("script", Description = "IdiotScript template with {name} placeholders, e.g. \"Ticker(X).IsAboveVwap().TakeProfit({tp}).StopLoss({sl})\".")]
    public string Script { get; init; } = "";

    [Option("grid", Description = "Repeatable: a placeholder and its values, e.g. --grid tp=1,2,3 --grid sl=0.5,1.")]
    public string[]? Grid { get; init; }

    [Option("from", Description = "First ET day (yyyy-MM-dd, default: 30 days before --to).")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Last ET day (yyyy-MM-dd, default: today ET).")]
    public DateOnly? To { get; init; }

    [Option("holdout", Description = "Fraction of the latest days kept out of selection and scored once on the chosen config (default 0.3).")]
    public double Holdout { get; init; } = 0.3;

    [Option("top", Description = "Configs to list (default 10).")]
    public int Top { get; init; } = 10;

    [Option("resamples", Description = "Reality-check bootstrap resamples (default 1000).")]
    public int Resamples { get; init; } = 1000;

    [Option("block", Description = "Mean bootstrap block length in symbol-days (default 5).")]
    public double Block { get; init; } = 5;

    [Option("seed", Description = "Bootstrap seed (default 1337).")]
    public int Seed { get; init; } = 1337;

    [Option("max-configs", Description = "Refuse grids larger than this (default 2000).")]
    public int MaxConfigs { get; init; } = 2000;
}

/// <summary>
/// Tries every combination of a strategy template's parameters and says how much of the winner's
/// result is likely luck: each config's naive p-value is corrected for the number tried (BH q-value),
/// its Sharpe is deflated by the best Sharpe expected from that many random configs, and White's reality
/// check / Hansen's SPA test the best config against the whole search. The winner is picked on the
/// selection days only, then scored once on held-out later days it never saw.
/// </summary>
[Operation("evidence search", Summary = "Grid-search a strategy template with multiple-testing corrections: BH q-values, deflated Sharpe, reality check/SPA, and a held-out score for the winner.", Risk = OperationRisk.Read)]
[Example("idiotproof evidence search SNDL MULN --script \"Ticker(X).IsAboveVwap().TakeProfit({tp}).StopLoss({sl})\" --grid tp=0.2,0.4 --grid sl=0.1,0.2")]
public sealed class EvidenceSearchOperation : IOperation<EvidenceSearchArgs>
{
    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public async Task<OperationResult> ExecuteAsync(EvidenceSearchArgs args, OperationContext ctx)
    {
        var symbols = EvidenceSupport.Symbols(args.Symbols);
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol.");
        if (string.IsNullOrWhiteSpace(args.Script)) return OperationResult.Usage("--script is required.");
        if (args.Holdout is < 0 or >= 1) return OperationResult.Usage("--holdout must be in [0, 1).");
        if (args.Resamples < 100) return OperationResult.Usage("--resamples must be >= 100.");
        if (args.Block < 1) return OperationResult.Usage("--block must be >= 1.");

        var grid = ParseGrid(args.Grid ?? []);
        if (grid.Error is not null) return OperationResult.Usage(grid.Error);
        var used = Placeholder.Matches(args.Script).Select(m => m.Groups[1].Value).Distinct().ToList();
        var missing = used.Where(n => !grid.Axes.ContainsKey(n)).ToList();
        if (missing.Count > 0) return OperationResult.Usage($"No --grid values for placeholder(s): {string.Join(", ", missing)}.");
        var unused = grid.Axes.Keys.Where(n => !used.Contains(n)).ToList();
        if (unused.Count > 0) return OperationResult.Usage($"--grid names not in the script: {string.Join(", ", unused)}.");

        var configs = Cartesian(used.Select(n => (n, grid.Axes[n])).ToList());
        if (configs.Count > args.MaxConfigs)
            return OperationResult.Usage($"{configs.Count} configs exceeds --max-configs {args.MaxConfigs}.");

        var definitions = new List<StrategyDefinition>(configs.Count);
        foreach (var config in configs)
        {
            var text = Placeholder.Replace(args.Script, m => config[m.Groups[1].Value]);
            var def = ScriptParser.ParseScript(text);
            if (def is null) return OperationResult.Usage($"Config {Label(config)} does not parse: {text}");
            definitions.Add(def);
        }

        var to = args.To ?? EvidenceSupport.TodayEt(ctx);
        var from = args.From ?? to.AddDays(-30);
        if (to < from) return OperationResult.Usage("--to must be on or after --from.");

        var bars = await ctx.Get<IHistoricalBarStore>()
            .GetBarsForSymbolsAsync(symbols, from, to, BarTimeframe.Minute1, ct: ctx.CancellationToken);

        // Cells are (day, symbol) pairs with bars; every config is scored on the same cells so the series align.
        var cells = new List<(DateOnly Day, string Symbol, IReadOnlyList<Candle> AllDay, decimal? PrevClose)>();
        foreach (var s in symbols)
        {
            if (!bars.TryGetValue(s, out var raw)) continue;
            decimal? prevClose = null;
            foreach (var g in raw.Where(c => c.IsValid && c.Close > 0).GroupBy(EvidenceSupport.DateEt).OrderBy(g => g.Key))
            {
                var day = g.OrderBy(c => c.StartUtc).ToList();
                cells.Add((g.Key, s, day, prevClose));
                prevClose = day[^1].Close;
            }
        }
        cells = cells.OrderBy(c => c.Day).ThenBy(c => c.Symbol, StringComparer.Ordinal).ToList();
        if (cells.Count < 4) return OperationResult.Usage("Too few symbol-days with bars to search over.");

        var days = cells.Select(c => c.Day).Distinct().OrderBy(d => d).ToList();
        int selectionDayCount = Math.Max(1, (int)Math.Round(days.Count * (1 - args.Holdout)));
        if (args.Holdout > 0 && selectionDayCount >= days.Count) selectionDayCount = days.Count - 1;
        var lastSelectionDay = days[selectionDayCount - 1];
        int selectionCells = cells.Count(c => c.Day <= lastSelectionDay);

        var returns = new double[configs.Count][];
        var tradeCounts = new int[configs.Count];
        var holdoutTrades = new int[configs.Count];
        var holdoutWins = new int[configs.Count];
        for (int k = 0; k < configs.Count; k++)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var def = definitions[k];
            var series = new double[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                def.Symbol = cell.Symbol;
                var (start, end) = BacktestDayRunner.SessionWindowUtc(cell.Day, def.Session);
                var window = cell.AllDay.Where(c => c.StartUtc >= start && c.StartUtc < end).ToList();
                if (window.Count == 0) continue;
                var report = StrategyBacktester.Run(def, window, new BacktestOptions { PreviousClose = cell.PrevClose });
                series[i] = report.Trades.Sum(t => (double)t.ReturnPercent) / 100.0;
                if (i < selectionCells) tradeCounts[k] += report.TradeCount;
                else { holdoutTrades[k] += report.TradeCount; holdoutWins[k] += report.Wins; }
            }
            returns[k] = series;
        }

        var selection = returns.Select(r => r[..selectionCells]).ToList();
        var moments = selection.Select(ReturnMoments.Of).ToList();
        var naiveP = selection.Select(MultipleTesting.MeanAboveZeroPValue).ToArray();
        var q = MultipleTesting.BenjaminiHochberg(naiveP);
        var sharpes = moments.Select(m => m.Sharpe).ToList();
        double sharpeVariance = sharpes.Count > 1 ? Math.Pow(EvidenceStats.StdDev(sharpes), 2) : 0;
        var dsr = moments.Select(m => MultipleTesting.DeflatedSharpe(m, configs.Count, sharpeVariance)).ToArray();
        var rc = RealityCheck.Run(selection, args.Resamples, args.Block, args.Seed);

        int best = rc.BestConfig;
        var holdout = returns[best][selectionCells..];
        bool hasHoldout = holdout.Length > 0;
        double holdoutMean = hasHoldout ? EvidenceStats.Mean(holdout) : double.NaN;
        double holdoutP = hasHoldout ? MultipleTesting.MeanAboveZeroPValue(holdout) : double.NaN;

        var ranked = Enumerable.Range(0, configs.Count).OrderByDescending(k => moments[k].Mean).Take(Math.Max(1, args.Top)).ToList();
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("#", Align.Right).Column("config").Column("trades", Align.Right).Column("mean/cell", Align.Right)
            .Column("sharpe", Align.Right).Column("p", Align.Right).Column("q(BH)", Align.Right).Column("DSR", Align.Right);
        var rows = new List<object>();
        int rank = 0;
        foreach (var k in ranked)
        {
            rank++;
            table.Row(rank.ToString(), Label(configs[k]), tradeCounts[k].ToString(), EvidenceSupport.Pct(moments[k].Mean, 3),
                moments[k].Sharpe.ToString("0.000"), P(naiveP[k]), P(q[k]), P(dsr[k]));
            rows.Add(new
            {
                Rank = rank, Config = configs[k], Trades = tradeCounts[k], MeanPerCell = EvidenceSupport.Round(moments[k].Mean),
                Sharpe = EvidenceSupport.Round(moments[k].Sharpe), NaiveP = EvidenceSupport.Round(naiveP[k]),
                QValue = EvidenceSupport.Round(q[k]), DeflatedSharpe = EvidenceSupport.Round(dsr[k]),
            });
        }

        string verdict = rc.SpaPValue < 0.05 && q[best] < 0.05
            ? "The best config survives the search corrections on the selection days."
            : "The best config does NOT survive the search corrections: treat it as luck until proven otherwise.";
        var human = $"{table.Render()}\n" +
            $"{configs.Count} configs tried on {selectionCells} selection symbol-days ({days[0]:yyyy-MM-dd}..{lastSelectionDay:yyyy-MM-dd}).\n" +
            $"best = {Label(configs[best])}: reality check p={P(rc.WhitePValue)}, SPA p={P(rc.SpaPValue)}, q={P(q[best])}, DSR={P(dsr[best])}\n" +
            (hasHoldout
                ? $"held out {cells.Count - selectionCells} symbol-days after {lastSelectionDay:yyyy-MM-dd}: mean {EvidenceSupport.Pct(holdoutMean, 3)}/cell, p={P(holdoutP)}, {holdoutWins[best]}/{holdoutTrades[best]} winning trades\n"
                : "no holdout (--holdout 0)\n") +
            verdict;

        return OperationResult.Ok(new
        {
            From = from, To = to, ConfigsTried = configs.Count, SelectionCells = selectionCells, HoldoutCells = cells.Count - selectionCells,
            LastSelectionDay = lastSelectionDay,
            Best = new
            {
                Config = configs[best], WhitePValue = rc.WhitePValue, SpaPValue = rc.SpaPValue, QValue = EvidenceSupport.Round(q[best]),
                DeflatedSharpe = EvidenceSupport.Round(dsr[best]),
                HoldoutMeanPerCell = EvidenceSupport.Round(holdoutMean), HoldoutP = EvidenceSupport.Round(holdoutP),
                HoldoutTrades = holdoutTrades[best], HoldoutWins = holdoutWins[best],
            },
            Survives = rc.SpaPValue < 0.05 && q[best] < 0.05,
            Top = rows,
        }, human);
    }

    private static string P(double v) => double.IsNaN(v) ? "—" : v < 0.001 ? v.ToString("0.0e0", CultureInfo.InvariantCulture) : v.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Label(IReadOnlyDictionary<string, string> config) =>
        string.Join(" ", config.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    internal static (Dictionary<string, string[]> Axes, string? Error) ParseGrid(IEnumerable<string> specs)
    {
        var axes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            int eq = spec.IndexOf('=');
            if (eq <= 0) return (axes, $"Bad --grid '{spec}': expected name=v1,v2.");
            var name = spec[..eq].Trim();
            var values = spec[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length == 0) return (axes, $"--grid {name} has no values.");
            if (values.Any(v => v.IndexOfAny(['(', ')', '"', '{', '}']) >= 0)) return (axes, $"--grid {name} values must be plain literals.");
            if (!axes.TryAdd(name, values.Distinct().ToArray())) return (axes, $"--grid {name} given twice.");
        }
        return (axes, null);
    }

    private static List<Dictionary<string, string>> Cartesian(List<(string Name, string[] Values)> axes)
    {
        var result = new List<Dictionary<string, string>> { new(StringComparer.Ordinal) };
        foreach (var (name, values) in axes)
            result = result.SelectMany(partial => values.Select(v => new Dictionary<string, string>(partial, StringComparer.Ordinal) { [name] = v })).ToList();
        return result;
    }
}
