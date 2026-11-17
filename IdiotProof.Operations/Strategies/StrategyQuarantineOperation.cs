using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Strategies;

public sealed record StrategyQuarantineArgs
{
    [Option("symbol", Description = "Only this symbol.")]
    public string? Symbol { get; init; }

    [Option("diff", Description = "Also print each proposed repair as a line diff of the stored canon against the canon re-derived from the script.")]
    public bool Diff { get; init; }
}

[Operation("strategy quarantine", Summary = "List strategies the Monitor refuses to evaluate (canon unreadable, or no canon and a script that doesn't parse), with the reason, why each was not repaired automatically, and the proposed repair.", Risk = OperationRisk.Read,
    Description = "A quarantined strategy never fires; one that is quarantined while holding a position has NO managed exit and is listed first. " +
                  "The Monitor repairs a quarantined strategy by itself only when it is active, flat, at step 1 of its sequence, Paper or Sandbox (Live needs quarantine.autorepair.live), " +
                  "its script has no errors, and the canon re-derived from the script loses nothing the stored canon expressed. Everything else is listed here with what blocks it. " +
                  "This command changes nothing; --diff shows the canon a repair would write.")]
[Example("idiotproof strategy quarantine")]
[Example("idiotproof strategy quarantine --symbol NVDA --diff")]
public sealed class StrategyQuarantineOperation : IOperation<StrategyQuarantineArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyQuarantineArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var all = await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken);
        var policy = await CanonRepairPolicy.ReadAsync(ctx.Get<SettingsRepository>(), ctx.CancellationToken);
        var rows = all
            .Where(s => args.Symbol is null || s.Symbol.Equals(args.Symbol, StringComparison.OrdinalIgnoreCase))
            .Select(s => (s, Load: StrategyRefs.Load(s)))
            .Where(x => x.Load.Definition is null)
            .Select(x =>
            {
                var plan = CanonRepair.Plan(x.s);
                var blockers = Blockers(x.s, plan, policy);
                return new
                {
                    x.s.Id,
                    Short = StrategyRefs.ShortGuid(x.s.Id),
                    x.s.Symbol,
                    x.s.Title,
                    Mode = OrderIntent.ParseMode(x.s.BrokerMode).ToString(),
                    x.s.IsActive,
                    Position = x.s.PositionQty,
                    Reason = x.Load.CanonicalError ?? (string.IsNullOrWhiteSpace(x.s.ScriptJson)
                        ? "no canon, and the script doesn't parse"
                        : "the canon can't be read"),
                    Kind = x.Load.CanonicalError is not null ? "canon" : "script",
                    // True when nothing known here stops the Monitor (a working broker order still can).
                    AutoRepairable = blockers.Count == 0,
                    NotRepairedBecause = blockers,
                    ProposedChanges = plan?.ProposedCanon is null ? [] : plan.Changes,
                    ProposedCanon = plan?.ProposedCanon,
                    ProposedDiff = args.Diff && plan?.ProposedCanon is { } proposed ? CanonRepair.Diff(x.s.ScriptJson, proposed) : null,
                };
            })
            .OrderByDescending(r => r.Position > 0).ThenByDescending(r => r.IsActive).ThenBy(r => r.Symbol)
            .ToList();

        if (rows.Count == 0)
            return OperationResult.Ok(rows, $"No quarantined strategies ({all.Count} checked).");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("sym").Column("title", maxWidth: 28).Column("mode").Column("on").Column("pos", Align.Right).Column("reason", maxWidth: 60);
        foreach (var r in rows)
            table.Row(r.Short, r.Symbol, r.Title, r.Mode, r.IsActive ? "●" : "·", r.Position == 0 ? "" : r.Position.ToString(), r.Reason);

        var detail = new List<string>();
        foreach (var r in rows)
        {
            detail.Add($"{r.Short} {r.Symbol} \"{r.Title}\"");
            detail.Add(r.AutoRepairable
                ? $"  automatic repair: nothing blocks it; the Monitor repairs it the next time it evaluates {r.Symbol} (not while paused or hibernating, and not while one of its orders is working)."
                : "  not repaired automatically: " + string.Join("; ", r.NotRepairedBecause) + ".");
            if (r.ProposedCanon is null)
                detail.Add("  proposed repair: none.");
            else
            {
                detail.Add("  proposed repair (canon re-derived from the script): " +
                           (r.ProposedChanges.Count == 0 ? "not safe to write automatically, for the reason above" : string.Join("; ", r.ProposedChanges)) + ".");
                if (r.ProposedDiff is { } diff)
                    detail.AddRange(diff.Select(l => "    " + l));
            }
        }

        var holding = rows.Count(r => r.Position > 0);
        var proposals = rows.Count(r => r.ProposedCanon is not null);
        var human = table.Render() + "\n\n" + string.Join("\n", detail) +
                    $"\n\n{rows.Count} of {all.Count} strategies quarantined." +
                    (holding > 0 ? $"\nWARNING: {holding} hold a position with no managed exit — manage them by hand (`idiotproof positions show <sym>`)." : "") +
                    (proposals > 0 && !args.Diff ? "\nRe-run with --diff to see each proposed canon change line by line." : "") +
                    "\nTo apply repairs by hand: `idiotproof strategy resync-canon` (dry run), then `--apply`; it rewrites every canon of yours that differs from its script and still refuses to lose a condition or branch.";
        return OperationResult.Ok(rows, human);
    }

    /// <summary>Everything that stops the Monitor from repairing this row by itself, most decisive first.</summary>
    private static List<string> Blockers(IdiotProof.Core.Data.Strategy s, CanonRepairPlan? plan, CanonRepairPolicy policy)
    {
        var blockers = new List<string>();
        if (plan?.StateBlocker is { } state) blockers.Add(state);
        if (plan is null) blockers.Add("the canon could not be examined");
        else if (plan.ContentBlocker is { } content) blockers.Add(content);
        if (policy.Blocker(s) is { } refused) blockers.Add(refused);
        if (!s.IsActive) blockers.Add("it is not active, and the Monitor only repairs strategies it evaluates");
        return blockers;
    }
}
