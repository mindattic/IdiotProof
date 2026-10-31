using IdiotProof.Core.Identity;
using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchScanArgs
{
    [Option("tickers", Description = "Scan just these symbols, comma-separated (default: every watchlist symbol plus the next rotating batch of the universe).")]
    public string[]? Tickers { get; init; }

    [Option("batch", Description = "Rotating-batch size beyond the watchlist, 1-5000 (default 300, or IDIOTPROOF_RESEARCHSCAN_BATCHSIZE).")]
    public int? Batch { get; init; }

    [Option("days-back", Description = "Lookback per source in days, 1-30 (default 2, or IDIOTPROOF_RESEARCHSCAN_DAYSBACK).")]
    public int? DaysBack { get; init; }
}

/// <summary>
/// Runs one research scan pass now, inside the Hub: the same pass the scheduled
/// <c>IdiotProof.ResearchScanner</c> task runs (news, 8-K and Form 4 extraction, the regulatory
/// and index-event scanners, outcome backfill, significance scoring), recorded as a ScanRun row.
/// Never part of the Monitor's trading loop. LLM extraction runs on the caller's Claude key.
/// </summary>
[Operation("research scan", Summary = "Run one research scan pass now in the Hub (news, SEC filings, regulatory, index events, backfill, scoring).",
    Description = "Targeted with --tickers; otherwise watchlist + the next rotating batch of the ticker universe. Writes claims and a ScanRun row; the Monitor's trading loop is untouched.",
    Risk = OperationRisk.Write)]
[Example("idiotproof research scan --tickers NVDA,AMD")]
[Example("idiotproof research scan --batch 50 --days-back 3")]
public sealed class ResearchScanOperation : IOperation<ResearchScanArgs>
{
    public async Task<OperationResult> ExecuteAsync(ResearchScanArgs args, OperationContext ctx)
    {
        if (args.Batch is < 1 or > 5000) return OperationResult.Usage("--batch must be 1-5000.");
        if (args.DaysBack is < 1 or > 30) return OperationResult.Usage("--days-back must be 1-30.");
        var tickers = (args.Tickers ?? [])
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (tickers.Any(t => t.Length > 20 || !t.All(c => char.IsLetterOrDigit(c) || c is '.' or '-')))
            return OperationResult.Usage("--tickers takes symbols like NVDA,BRK.B.");

        // The caller's own Claude key when there is one; the host key otherwise.
        string? claudeKey = null;
        if (ctx.Services.GetService<IClaudeKeyContext>() is { } keys)
            claudeKey = (await keys.ResolveAsync(ctx.CancellationToken)).ClaudeApiKey;

        var run = await ctx.Get<IResearchScanPass>().RunAsync(
            new ScanPassOptions(tickers.Count > 0 ? tickers : null, args.Batch, args.DaysBack, claudeKey),
            ctx.CancellationToken);

        var seconds = run.CompletedUtc is { } done ? (done - run.StartedUtc).TotalSeconds : 0;
        var data = new
        {
            run.Id,
            Targeted = tickers.Count > 0,
            Tickers = tickers,
            run.TickersScanned,
            run.UniverseSize,
            run.ClaimsFound,
            run.ErrorCount,
            Notes = run.Notes?.Trim(),
            run.StartedUtc,
            run.CompletedUtc,
            DurationSeconds = Math.Round(seconds, 1),
        };

        var kv = new KeyValueBlock()
            .Add("scan run", run.Id.ToString())
            .Add("covered", tickers.Count > 0
                ? $"{run.TickersScanned}/{tickers.Count} named tickers ({string.Join(",", tickers)})"
                : $"{run.TickersScanned} tickers (watchlist + rotating batch; universe {Fmt.Number(run.UniverseSize)})")
            .Add("claims", $"{run.ClaimsFound} new")
            .Add("errors", run.ErrorCount == 0 ? "none" : $"{run.ErrorCount} (see the Hub log)")
            .Add("notes", string.IsNullOrWhiteSpace(run.Notes) ? null : run.Notes.Trim())
            .Add("ran", $"{Fmt.Et(run.StartedUtc)} for {seconds:0.0}s");
        var tail = run.ClaimsFound > 0 ? "\n`idiotproof research claims` shows the ranked feed." : "";
        return OperationResult.Ok(data, kv.Render() + tail);
    }
}
