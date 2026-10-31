using IdiotProof.Blazor.Services;

namespace IdiotProof.Operations.Risk;

public sealed record RiskSetArgs
{
    [Option("max-loss-per-trade", Description = "Most a single trade may lose in the worst case, in dollars.")]
    public decimal? MaxLossPerTrade { get; init; }

    [Option("max-loss-per-day", Description = "Daily loss that trips the circuit breaker, in dollars (at least the per-trade limit).")]
    public decimal? MaxLossPerDay { get; init; }

    [Option("min-stop-percent", Description = "Tightest allowed stop, percent of entry (0.01-50).")]
    public decimal? MinStopPercent { get; init; }

    [Option("max-stop-percent", Description = "Widest allowed stop, percent of entry (at least the minimum).")]
    public decimal? MaxStopPercent { get; init; }

    [Option("account-balance", Description = "Balance the position size is measured against, in dollars.")]
    public decimal? AccountBalance { get; init; }

    [Option("max-account-risk-percent", Description = "Most of the balance one trade may risk, percent (0-100).")]
    public decimal? MaxAccountRiskPercent { get; init; }
}

/// <summary>
/// Edits the owner's Risk Guardian limits (the Settings page's fields). Unnamed limits keep their
/// value. The cached Guardian's config is expired at once, so the Monitor and the gateway in this
/// Hub use the new limits on their next entry; another process picks them up within the 2-minute
/// refresh. The day's loss counter is never reset by a change (IP-LAW-2).
/// </summary>
[Operation("risk set", Summary = "Change Risk Guardian limits; unnamed ones keep their value and today's loss counter is preserved.", Risk = OperationRisk.Write)]
[Example("idiotproof risk set --max-loss-per-trade 50 --max-loss-per-day 200")]
[Example("idiotproof risk set --max-stop-percent 8 --account-balance 25000")]
public sealed class RiskSetOperation : IOperation<RiskSetArgs>
{
    public async Task<OperationResult> ExecuteAsync(RiskSetArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args is { MaxLossPerTrade: null, MaxLossPerDay: null, MinStopPercent: null, MaxStopPercent: null, AccountBalance: null, MaxAccountRiskPercent: null })
            return OperationResult.Usage("Name at least one limit, e.g. `idiotproof risk set --max-loss-per-trade 50`. `idiotproof risk show` lists them.");

        var before = await RiskReads.LimitsAsync(ctx, owner);
        var want = before with
        {
            MaxLossPerTrade = args.MaxLossPerTrade ?? before.MaxLossPerTrade,
            MaxLossPerDay = args.MaxLossPerDay ?? before.MaxLossPerDay,
            MinStopLossPercent = args.MinStopPercent ?? before.MinStopLossPercent,
            MaxStopLossPercent = args.MaxStopPercent ?? before.MaxStopLossPercent,
            AccountBalance = args.AccountBalance ?? before.AccountBalance,
            MaxAccountRiskPercent = args.MaxAccountRiskPercent ?? before.MaxAccountRiskPercent,
        };

        // Refuse what the Settings page would silently clamp: a person typing a limit should get it or be told why not.
        var problems = new List<string>();
        if (want.MaxLossPerTrade <= 0) problems.Add("--max-loss-per-trade must be more than $0");
        if (want.MaxLossPerDay < want.MaxLossPerTrade) problems.Add($"--max-loss-per-day ({want.MaxLossPerDay}) can't be below the per-trade limit ({want.MaxLossPerTrade})");
        if (want.MinStopLossPercent is < 0.01m or > 50m) problems.Add("--min-stop-percent must be 0.01-50");
        if (want.MaxStopLossPercent < want.MinStopLossPercent) problems.Add($"--max-stop-percent ({want.MaxStopLossPercent}) can't be below the minimum ({want.MinStopLossPercent})");
        if (want.AccountBalance <= 0) problems.Add("--account-balance must be more than $0 (the account-risk check fails closed at 0)");
        if (want.MaxAccountRiskPercent is <= 0 or > 100) problems.Add("--max-account-risk-percent must be more than 0 and at most 100");
        if (problems.Count > 0) return OperationResult.Usage(string.Join("; ", problems) + ".");

        await ctx.Get<UserPreferencesService>().SetRiskConfigAsync(owner, want.MaxLossPerTrade, want.MaxLossPerDay,
            want.MinStopLossPercent, want.MaxStopLossPercent, want.AccountBalance, want.MaxAccountRiskPercent, ctx.CancellationToken);
        ctx.Get<RiskGuardianService>().Invalidate(owner);
        var after = await RiskReads.LimitsAsync(ctx, owner);

        var changed = new List<string>();
        void Diff(string name, decimal a, decimal b) { if (a != b) changed.Add($"{name} {a:0.##} -> {b:0.##}"); }
        Diff("max loss/trade", before.MaxLossPerTrade, after.MaxLossPerTrade);
        Diff("max loss/day", before.MaxLossPerDay, after.MaxLossPerDay);
        Diff("min stop %", before.MinStopLossPercent, after.MinStopLossPercent);
        Diff("max stop %", before.MaxStopLossPercent, after.MaxStopLossPercent);
        Diff("balance", before.AccountBalance, after.AccountBalance);
        Diff("account risk %", before.MaxAccountRiskPercent, after.MaxAccountRiskPercent);

        var head = changed.Count == 0 ? "Nothing changed." : "Changed: " + string.Join(", ", changed) + ".";
        return OperationResult.Ok(new { before, after, changed },
            head + "\n" + after.Render() +
            "\nIn effect for this Hub's next entry (other processes within 2 minutes); today's loss counter is kept.");
    }
}
