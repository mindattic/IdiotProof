using IdiotProof.Blazor.Services;

namespace IdiotProof.Operations.Strategies;

public sealed record StrategyResyncCanonArgs
{
    [Option("apply", Description = "Write the repaired canons (default is a dry run that changes nothing).")]
    public bool Apply { get; init; }
}

[Operation("strategy resync-canon", Summary = "Re-derive your strategies' canons from their scripts where the script now yields a richer canon; dry run unless --apply.", Risk = OperationRisk.Write,
    Description = "The repair for verbs an older parser dropped silently. A row is rewritten only when the re-derived canon has at least as many entry conditions and branches " +
                  "as the stored one; anything that would lose a condition is skipped and reported. Only the canon (ScriptJson) changes — position bookkeeping is untouched.")]
[Example("idiotproof strategy resync-canon")]
[Example("idiotproof strategy resync-canon --apply")]
public sealed class StrategyResyncCanonOperation : IOperation<StrategyResyncCanonArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyResyncCanonArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var r = await ctx.Get<StrategyRepository>().ResyncCanonFromTextAsync(args.Apply, ctx.CancellationToken, owner);

        var lines = new List<string>
        {
            $"Canon resync ({(args.Apply ? "APPLIED" : "dry run")}) — scanned {r.Scanned}, {(args.Apply ? "fixed" : "would fix")} {r.Changed}, " +
            $"skipped to avoid a regression {r.SkippedRegression}.",
        };
        lines.AddRange(r.Notes.Select(n => "  " + n));
        if (r.Notes.Count == 0) lines.Add("  Every canon already matches its script.");
        if (!args.Apply && r.Changed > 0) lines.Add("Re-run with --apply to write the changes.");

        return OperationResult.Ok(new { Applied = args.Apply, r.Scanned, r.Changed, r.SkippedRegression, r.Notes }, string.Join("\n", lines));
    }
}
