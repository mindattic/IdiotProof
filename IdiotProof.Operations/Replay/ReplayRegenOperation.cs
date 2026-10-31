using IdiotProof.Blazor.Data;
using IdiotProof.Monitor;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Replay;

public sealed record ReplayRegenArgs
{
    [Option("out", Description = "Archive folder to (re)write (default the mindattic.com replays folder).")]
    public string? Out { get; init; }
}

/// <summary>
/// Regenerates the whole replay archive — every run page, every ticker index and the root index —
/// from SQL, proving the database is authoritative. HTML only: it never uploads or deploys
/// anything; publishing the folder stays a separate, manual step.
/// </summary>
[Operation("replay regen", Summary = "Regenerate the replay archive's HTML (every run page and index) from SQL; never uploads or deploys.", Risk = OperationRisk.Write)]
[Example("idiotproof replay regen")]
[Example("idiotproof replay regen --out C:\\temp\\replays")]
public sealed class ReplayRegenOperation : IOperation<ReplayRegenArgs>
{
    public async Task<OperationResult> ExecuteAsync(ReplayRegenArgs args, OperationContext ctx)
    {
        var outRoot = ReplayKit.ArchiveRoot(args.Out);
        var (runs, tickers) = await StrategyReplay.RegenerateArchiveAsync(ctx.Get<IDbContextFactory<AppDbContext>>(), outRoot, ctx.CancellationToken);
        return OperationResult.Ok(new { Runs = runs, Tickers = tickers, Archive = outRoot, Index = Path.Combine(outRoot, "index.htm") },
            $"Regenerated {runs} run page(s) across {tickers} ticker(s) from SQL → {outRoot}\nNothing was published; uploading the folder is a separate, manual step.");
    }
}
