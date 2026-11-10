using IdiotProof.Core.Research.Momentum;

namespace IdiotProof.Operations.Research;

// IP-T493 "Momentum." claim tracker. The same four steps run in-process from tools/MomentumTracker when the
// Hub has not been republished with these operations. Research only: GET-only Alpaca data reads, no orders.

public sealed record MomentumImportArgs
{
    [Option("dir", Description = @"The collector's output folder (default D:\IdiotProofData\momentum).")]
    public string? Dir { get; init; }
}

[Operation("momentum import", Summary = "Upsert the Momentum. (@trade.momentum) shorts and their caption transcripts from the collector's JSON files into MomentumVideos / MomentumTranscripts (idempotent; a changed transcript is queued for re-extraction).", Risk = OperationRisk.Write)]
[Example("idiotproof momentum import")]
[Example(@"idiotproof momentum import --dir D:\IdiotProofData\momentum")]
public sealed class MomentumImportOperation : IOperation<MomentumImportArgs>
{
    public async Task<OperationResult> ExecuteAsync(MomentumImportArgs args, OperationContext ctx)
    {
        var r = await ctx.Get<MomentumTracker>().ImportAsync(args.Dir, ctx.CancellationToken);
        return r.Ok ? OperationResult.Ok(r.Data, r.Human) : OperationResult.Fail(r.Human, r.Data);
    }
}

public sealed record MomentumExtractArgs
{
    [Option("since", Description = "Only transcripts with a trade date on or after this ET date, yyyy-MM-dd.")]
    public DateOnly? Since { get; init; }

    [Option("limit", Description = "At most this many transcripts this run (0 = all), newest first.")]
    public int Limit { get; init; }

    [Option("force", Description = "Re-extract transcripts that already have claims (their claims and outcomes are replaced).")]
    public bool Force { get; init; }

    [Option("dir", Description = @"Data folder for the market-data caches (default D:\IdiotProofData\momentum).")]
    public string? Dir { get; init; }
}

[Operation("momentum extract", Summary = "Extract claims (ranked picks with trigger / confirm / target levels, setup tags, brags, market statements) from every transcript without claims yet, newest first, with a Haiku-class model through Legion on your Claude key; tickers validated against Alpaca assets and movers, spoken prices scaled against real bars.", Risk = OperationRisk.Write)]
[Example("idiotproof momentum extract")]
[Example("idiotproof momentum extract --since 2026-11-01 --limit 10")]
public sealed class MomentumExtractOperation : IOperation<MomentumExtractArgs>
{
    public async Task<OperationResult> ExecuteAsync(MomentumExtractArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args.Limit < 0) return OperationResult.Usage("--limit must be 0 or more.");
        var r = await ctx.Get<MomentumTracker>().ExtractAsync(owner, args.Since, args.Limit, args.Dir, ctx.CancellationToken, args.Force);
        return r.Ok ? OperationResult.Ok(r.Data, r.Human) : OperationResult.Fail(r.Human, r.Data);
    }
}

public sealed record MomentumVerifyArgs
{
    [Option("since", Description = "Only claims with a trade date on or after this ET date, yyyy-MM-dd.")]
    public DateOnly? Since { get; init; }

    [Option("force", Description = "Re-verify claims that already have outcomes.")]
    public bool Force { get; init; }

    [Option("dir", Description = @"Data folder for the market-data caches (default D:\IdiotProofData\momentum).")]
    public string? Dir { get; init; }
}

[Operation("momentum verify", Summary = "Check every claim whose session has finished against Alpaca 1-minute SIP bars (04:00-20:00 ET, plus the next session for day-2 language): trigger, confirm, targets, invalidation, run/drawdown, hypothetical plan P&L, verdict, gainer rank; brags against split-adjusted daily bars.", Risk = OperationRisk.Write)]
[Example("idiotproof momentum verify")]
[Example("idiotproof momentum verify --since 2026-11-01 --force")]
public sealed class MomentumVerifyOperation : IOperation<MomentumVerifyArgs>
{
    public async Task<OperationResult> ExecuteAsync(MomentumVerifyArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var r = await ctx.Get<MomentumTracker>().VerifyAsync(owner, args.Since, args.Force, args.Dir, ctx.CancellationToken);
        return r.Ok ? OperationResult.Ok(r.Data, r.Human) : OperationResult.Fail(r.Human, r.Data);
    }
}

public sealed record MomentumReportArgs
{
    [Option("since", Description = "Only claims with a trade date on or after this ET date, yyyy-MM-dd.")]
    public DateOnly? Since { get; init; }

    [Option("out", Description = "Where to write the markdown report (default docs/research/momentum-claims-report.md in the repo, else the data folder).")]
    public string? Out { get; init; }
}

[Operation("momentum report", Summary = "The Momentum. claim report, times in ET: counts overall and per month, trigger / target / stop rates, mean and median hypothetical plan P&L, share of picks that were real top gappers, brag accuracy, setup tags by use and by result, and every pick newest first; rewrites docs/research/momentum-claims-report.md.", Risk = OperationRisk.Read)]
[Example("idiotproof momentum report")]
[Example("idiotproof momentum report --since 2026-11-01")]
public sealed class MomentumReportOperation : IOperation<MomentumReportArgs>
{
    public async Task<OperationResult> ExecuteAsync(MomentumReportArgs args, OperationContext ctx)
    {
        var r = await ctx.Get<MomentumTracker>().ReportAsync(args.Since, args.Out, ctx.CancellationToken);
        return r.Ok ? OperationResult.Ok(r.Data, r.Human) : OperationResult.Fail(r.Human, r.Data);
    }
}
