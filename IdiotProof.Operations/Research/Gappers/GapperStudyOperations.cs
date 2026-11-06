using IdiotProof.Core.Research.Gappers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Research.Gappers;

/// <summary>Shared pieces of the <c>research gappers *</c> operations (IP-T442): the research-DB guard and the ET stamp.</summary>
internal static class GapperStudyOps
{
    public const string ResearchOnly = "Research rows only, on the research database (the connection string must name a database containing '_Research_'); read-only market data; no orders.";

    public static OperationResult? Guard(GapperStudyServices s)
    {
        var error = s.GuardError();
        return error is null ? null : OperationResult.Blocked(error);
    }

    public static List<string> Symbols(string? csv) =>
        (csv ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant()).Distinct().ToList();

    public static OperationResult? CheckSplit(string split) =>
        split.ToLowerInvariant() is "train" or "validation" or "holdout" ? null : OperationResult.Usage("--split must be train, validation or holdout.");
}

public sealed record GapperBuildUniverseArgs
{
    [Option("from", Description = "First trading day D of the universe (ET, yyyy-MM-dd).", Required = true)]
    public DateOnly From { get; init; }

    [Option("to", Description = "Last trading day D (ET, yyyy-MM-dd).", Required = true)]
    public DateOnly To { get; init; }

    [Option("smoke", Description = "Smoke run: only --symbols, daily bars from 60 days before --from, rows tagged split 'smoke', sample.smoke.json.")]
    public bool Smoke { get; init; }

    [Option("symbols", Description = "Comma-separated symbols for a --smoke run.")]
    public string? Symbols { get; init; }

    [Option("max-calls", Description = "Stop pulling after this many Alpaca requests (0 = no cap); the run stays resumable.")]
    public int MaxCalls { get; init; }
}

[Operation("research gappers build-universe", Summary = "Gapper study T1/T6 + universe: every Alpaca us_equity asset's daily bars and splits, then U(D), exclusions, candidates, ADV deciles and price tiers per the frozen protocol, and sample.json.", Risk = OperationRisk.Read,
    Description = GapperStudyOps.ResearchOnly + " Every number comes from docs/research/gapper-study-2026-11/params.json. Resumable: covered symbols are not re-pulled; universe rows for the range are rebuilt.")]
[Example("idiotproof research gappers build-universe --from 2021-11-01 --to 2026-10-30")]
[Example("idiotproof research gappers build-universe --smoke --symbols GOGO,STAK,BE --from 2026-11-03 --to 2026-11-05")]
public sealed class GapperBuildUniverseOperation : IOperation<GapperBuildUniverseArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperBuildUniverseArgs args, OperationContext ctx)
    {
        if (args.To < args.From) return OperationResult.Usage("--to is before --from.");
        var symbols = GapperStudyOps.Symbols(args.Symbols);
        if (args.Smoke && symbols.Count == 0) return OperationResult.Usage("--smoke needs --symbols.");
        if (!args.Smoke && symbols.Count > 0) return OperationResult.Usage("--symbols is only for --smoke runs; the real universe is every asset.");
        var s = ctx.Get<GapperStudyServices>();
        if (GapperStudyOps.Guard(s) is { } blocked) return blocked;

        var r = await ctx.Get<GapperUniverseStage>().RunAsync(new UniverseStageRequest(args.From, args.To, args.Smoke, symbols, ctx.OwnerId, args.MaxCalls), ctx.CancellationToken);
        var kv = new KeyValueBlock()
            .Add("keys", r.KeySource)
            .Add("assets", r.Assets.ToString())
            .Add("daily bars", $"{r.DailyBarsInserted} rows inserted in {r.DailyBarCalls} calls")
            .Add("splits", $"{r.SplitsInserted} rows inserted in {r.SplitCalls} calls")
            .Add("symbols with bars", $"{r.Symbols} ({r.ReusedTickerSymbols} bracket a >90-session gap)")
            .Add("universe days", r.UniverseDays.ToString())
            .Add("candidates", r.Candidates.ToString())
            .Add("excluded", string.Join(", ", r.ExcludedByReason.Select(kv2 => $"{kv2.Key}={kv2.Value}")), "none")
            .Add("minutes", r.Minutes.ToString("0.00"))
            .Add("sample", r.SampleFile);
        return OperationResult.Ok(r, kv.Render());
    }
}

public sealed record GapperPullArgs
{
    [Option("tier", Description = "bars (T2 minute bars + gapper classification + controls), news (T3), cik (T5), edgar (T4 form index + acceptance times), splits (T6), or all.", Required = true)]
    public string Tier { get; init; } = "all";

    [Option("smoke", Description = "Operate on the smoke-tagged universe rows only.")]
    public bool Smoke { get; init; }

    [Option("from", Description = "Restrict to candidate days on or after this ET date.")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Restrict to candidate days on or before this ET date.")]
    public DateOnly? To { get; init; }

    [Option("max-calls", Description = "Stop after this many API requests per tier (0 = no cap); the pull resumes from its coverage marks next time.")]
    public int MaxCalls { get; init; }
}

[Operation("research gappers pull", Summary = "Gapper study data tiers: minute bars for candidate/prior/control days into the file store (then gapper tiers and controls), Alpaca news, ticker-CIK map, EDGAR form index with acceptance times, splits. Resumable, rate-limited, --max-calls capped.", Risk = OperationRisk.Read,
    Description = GapperStudyOps.ResearchOnly + " Minute bars go to D:\\IdiotProofData\\gapper-study\\bars (setting gapperStudy.barRoot) as packed day blocks; a killed pull is rerun, not restarted.")]
[Example("idiotproof research gappers pull --tier bars")]
[Example("idiotproof research gappers pull --tier all --smoke")]
[Example("idiotproof research gappers pull --tier edgar --max-calls 5000")]
public sealed class GapperPullOperation : IOperation<GapperPullArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperPullArgs args, OperationContext ctx)
    {
        var tier = args.Tier.Trim().ToLowerInvariant();
        if (tier is not ("bars" or "news" or "cik" or "edgar" or "splits" or "all")) return OperationResult.Usage("--tier must be bars, news, cik, edgar, splits or all.");
        var s = ctx.Get<GapperStudyServices>();
        if (GapperStudyOps.Guard(s) is { } blocked) return blocked;

        var tiers = await ctx.Get<GapperPullStage>().RunAsync(new PullStageRequest(tier, args.Smoke, ctx.OwnerId, args.MaxCalls, args.From, args.To), ctx.CancellationToken);
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("tier").Column("rows", Align.Right).Column("calls", Align.Right).Column("minutes", Align.Right).Column("coverage", Align.Right).Column("note");
        foreach (var t in tiers)
            table.Row(t.Tier, t.Rows.ToString(), t.Calls.ToString(), t.Minutes.ToString("0.00"), t.CoveragePct is { } c ? $"{c:0.0}%" : "-", t.Note);
        return OperationResult.Ok(tiers, table.Render());
    }
}

public sealed record GapperCatalystsArgs
{
    [Option("llm-budget", Description = "At most this many never-classified backfilled rows get a Haiku classification (default 0).")]
    public int LlmBudget { get; init; }
}

[Operation("research gappers catalysts", Summary = "Re-run the deterministic CatalystPreClassifier over every backfilled catalyst row (idempotent) and spend an optional Haiku budget on rows never classified; each article/filing is classified at most once.", Risk = OperationRisk.Read,
    Description = GapperStudyOps.ResearchOnly)]
[Example("idiotproof research gappers catalysts")]
[Example("idiotproof research gappers catalysts --llm-budget 200")]
public sealed class GapperCatalystsOperation : IOperation<GapperCatalystsArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperCatalystsArgs args, OperationContext ctx)
    {
        if (args.LlmBudget < 0) return OperationResult.Usage("--llm-budget must be 0 or more.");
        var s = ctx.Get<GapperStudyServices>();
        if (GapperStudyOps.Guard(s) is { } blocked) return blocked;
        var r = await ctx.Get<GapperCatalystStage>().RunAsync(args.LlmBudget, ctx.CancellationToken);
        var kv = new KeyValueBlock()
            .Add("backfilled rows", r.Rows.ToString())
            .Add("re-classified", r.Reclassified.ToString())
            .Add("LLM classified", $"{r.LlmClassified} (budget {args.LlmBudget}; {r.LlmSkipped} still unclassified)")
            .Add("minutes", r.Minutes.ToString("0.00"));
        return OperationResult.Ok(r, kv.Render());
    }
}

public sealed record GapperSnapshotArgs
{
    [Option("split", Description = "train, validation or holdout (holdout needs holdout-unlock.txt and runs once).", Required = true)]
    public string Split { get; init; } = "train";

    [Option("smoke", Description = "Snapshot the smoke-tagged rows instead (profile goes to data/tod-profile.smoke.json).")]
    public bool Smoke { get; init; }

    [Option("from", Description = "Restrict to days on or after this ET date.")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Restrict to days on or before this ET date.")]
    public DateOnly? To { get; init; }
}

[Operation("research gappers snapshot", Summary = "Gapper study feature snapshots: every candidate and control day at every frozen decision time through the shared GapperFormationFeatures.Compute, written as Source=backtest rows; fits and saves data/tod-profile.json on train control days first.", Risk = OperationRisk.Read,
    Description = GapperStudyOps.ResearchOnly + " The holdout split refuses to run unless holdout-unlock.txt holds the frozen commit hash, and refuses a second run.")]
[Example("idiotproof research gappers snapshot --split train")]
[Example("idiotproof research gappers snapshot --split train --smoke")]
public sealed class GapperSnapshotOperation : IOperation<GapperSnapshotArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperSnapshotArgs args, OperationContext ctx)
    {
        if (GapperStudyOps.CheckSplit(args.Split) is { } usage) return usage;
        var s = ctx.Get<GapperStudyServices>();
        if (GapperStudyOps.Guard(s) is { } blocked) return blocked;
        try
        {
            var r = await ctx.Get<GapperSnapshotStage>().RunAsync(new SnapshotStageRequest(args.Split, args.Smoke, args.From, args.To), ctx.CancellationToken);
            var kv = new KeyValueBlock()
                .Add("split", r.Split)
                .Add("symbol-days", r.SymbolDays.ToString())
                .Add("snapshot rows", $"{r.Rows} ({r.WithProfile} with VolRatioTod)")
                .Add("profile", r.ProfileBuilt ? $"fit on {r.ProfileDays} days → {r.ProfileFile}" : File.Exists(r.ProfileFile) ? $"loaded {r.ProfileFile}" : "none (VolRatioTod null)")
                .Add("holdout", r.HoldoutNote, "not a holdout run")
                .Add("minutes", r.Minutes.ToString("0.00"));
            return OperationResult.Ok(r, kv.Render());
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Holdout locked", StringComparison.Ordinal) || ex.Message.Contains("does not exist; run", StringComparison.Ordinal))
        {
            return OperationResult.Blocked(ex.Message);
        }
    }
}

public sealed record GapperLabelArgs
{
    [Option("split", Description = "train, validation or holdout (holdout needs holdout-unlock.txt and runs once).", Required = true)]
    public string Split { get; init; } = "train";

    [Option("smoke", Description = "Label the smoke-tagged rows instead.")]
    public bool Smoke { get; init; }

    [Option("from", Description = "Restrict to days on or after this ET date.")]
    public DateOnly? From { get; init; }

    [Option("to", Description = "Restrict to days on or before this ET date.")]
    public DateOnly? To { get; init; }
}

[Operation("research gappers label", Summary = "Gapper study labels: next-bar-open entry and forward returns per horizon, reach-20 by 09:45, premarket/RTH/full-session high times and bucket, fade, extension from open, velocity death, death-to-HOD, halt proxy and VWAP-loss time, per the frozen definitions.", Risk = OperationRisk.Read,
    Description = GapperStudyOps.ResearchOnly + " Same holdout lock as snapshot.")]
[Example("idiotproof research gappers label --split train")]
[Example("idiotproof research gappers label --split train --smoke")]
public sealed class GapperLabelOperation : IOperation<GapperLabelArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperLabelArgs args, OperationContext ctx)
    {
        if (GapperStudyOps.CheckSplit(args.Split) is { } usage) return usage;
        var s = ctx.Get<GapperStudyServices>();
        if (GapperStudyOps.Guard(s) is { } blocked) return blocked;
        try
        {
            var r = await ctx.Get<GapperLabelStage>().RunAsync(new LabelStageRequest(args.Split, args.Smoke, args.From, args.To), ctx.CancellationToken);
            var kv = new KeyValueBlock()
                .Add("split", r.Split)
                .Add("symbol-days", r.SymbolDays.ToString())
                .Add("label rows", $"{r.Rows} ({r.WithEntry} with an entry bar)")
                .Add("holdout", r.HoldoutNote, "not a holdout run")
                .Add("minutes", r.Minutes.ToString("0.00"));
            return OperationResult.Ok(r, kv.Render());
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Holdout locked", StringComparison.Ordinal))
        {
            return OperationResult.Blocked(ex.Message);
        }
    }
}

public sealed record GapperStatusArgs;

[Operation("research gappers status", Summary = "What the gapper study holds per split (universe days, candidates, gappers per tier, controls, minute-bar coverage, snapshots, labels), the data tiers' row counts, and the holdout lock state.", Risk = OperationRisk.Read,
    Description = GapperStudyOps.ResearchOnly)]
[Example("idiotproof research gappers status")]
public sealed class GapperStatusOperation : IOperation<GapperStatusArgs>
{
    public async Task<OperationResult> ExecuteAsync(GapperStatusArgs args, OperationContext ctx)
    {
        var s = ctx.Get<GapperStudyServices>();
        if (GapperStudyOps.Guard(s) is { } blocked) return blocked;
        var r = await ctx.Get<GapperStatusStage>().RunAsync(ctx.CancellationToken);
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("split").Column("universe days", Align.Right).Column("candidates", Align.Right).Column("tier20", Align.Right).Column("tier50", Align.Right)
            .Column("tier100", Align.Right).Column("controls", Align.Right).Column("bars %", Align.Right).Column("snapshots", Align.Right).Column("labels", Align.Right).Column("first").Column("last");
        foreach (var sp in r.Splits)
            table.Row(sp.Split, sp.UniverseDays.ToString(), sp.Candidates.ToString(), sp.Tier20.ToString(), sp.Tier50.ToString(), sp.Tier100.ToString(), sp.Controls.ToString(),
                sp.BarCoveragePct.ToString("0.0"), sp.Snapshots.ToString(), sp.Labels.ToString(), sp.First?.ToString("yyyy-MM-dd"), sp.Last?.ToString("yyyy-MM-dd"));
        var kv = new KeyValueBlock()
            .Add("database", r.Database)
            .Add("bar root", r.BarRoot)
            .Add("study dir", r.StudyDir)
            .Add("frozen commit", r.FrozenCommit, "unknown")
            .Add("frozen", r.FrozenUtc is { } f ? ctx.Et(f) : null, "NOT FROZEN")
            .Add("daily bars", $"{r.DailyBarRows} rows, {r.DailySymbols} symbols")
            .Add("splits", r.SplitRows.ToString())
            .Add("cik map", $"{r.CikMapped}/{r.CikTotal} mapped")
            .Add("news rows", r.NewsRows.ToString())
            .Add("edgar rows", $"{r.EdgarRows} ({r.EdgarWithAcceptance} with acceptance time)")
            .Add("profile", r.ProfileExists ? "data/tod-profile.json present" : "not fit yet")
            .Add("holdout", r.HoldoutState);
        return OperationResult.Ok(r, (r.Splits.Count == 0 ? "No universe rows yet." : table.Render()) + "\n" + kv.Render());
    }
}
