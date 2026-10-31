using IdiotProof.Models;
using IdiotProof.Operations.Indicators;
using IdiotProof.Operations.Rendering;
using IdiotProof.Operations.Strategies;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Llm;

public sealed record LlmVoteArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id (IP-XXXX-XXXX) or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("entry", Description = "Entry price to put to the panel (default: the last 1-minute close).")]
    public decimal? Entry { get; init; }
}

/// <summary>One counted vote in the payload.</summary>
public sealed record LlmVoteRow(string Voter, string Provider, string Decision, decimal Confidence, string Reasoning);

/// <summary>
/// A dry run of the LLM gate (IP-LAW-1): puts the strategy's entry, as the Monitor would build it
/// right now (last close, the strategy's stop and targets, the last 15 one-minute bars), to the
/// real voter panel and reports every vote and the consensus at the saved threshold. It never
/// places, sizes or arms anything, and it runs even when voting is off (it says so). Spends the
/// panel's LLM calls on your Claude key.
/// </summary>
[Operation("llm vote", Summary = "Dry-run the LLM voter panel on a strategy's current entry: every vote and the consensus. Never places an order.", Risk = OperationRisk.Read)]
[Example("idiotproof llm vote NVDA")]
[Example("idiotproof llm vote 0193a2b4 --entry 101.25")]
public sealed class LlmVoteOperation : IOperation<LlmVoteArgs>
{
    public async Task<OperationResult> ExecuteAsync(LlmVoteArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var strategy = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var def = StrategyRefs.RequireDefinition(strategy);
        var credentials = await ctx.Get<UserClaudeKeyResolver>().ResolveAsync(owner, ctx.CancellationToken);
        if (string.IsNullOrWhiteSpace(credentials.ClaudeApiKey))
            return OperationResult.Blocked(UserClaudeKeyResolver.NoKeyMessage + " (`idiotproof llm keys`)");

        var candles = await IndicatorBars.LoadAsync(ctx, def.Symbol, TimeSpan.FromMinutes(1), 60);
        var entry = args.Entry ?? candles[^1].Close;
        if (entry <= 0) return OperationResult.Usage("--entry must be positive.");
        var isLong = def.Direction == TradeDirection.Long;
        decimal? stop = def.StopLossPrice is > 0 ? (decimal)def.StopLossPrice.Value
            : def.StopLossPercent is > 0 ? Math.Round(entry * (1m + (isLong ? -1m : 1m) * (decimal)def.StopLossPercent.Value / 100m), 2)
            : def.TrailingStopPercent is > 0 ? Math.Round(entry * (1m + (isLong ? -1m : 1m) * (decimal)def.TrailingStopPercent.Value / 100m), 2)
            : null;
        var targets = new List<decimal>();
        if (def.TakeProfitPrice is > 0) targets.Add((decimal)def.TakeProfitPrice.Value);
        else if (def.TakeProfitPercent is > 0) targets.Add(Math.Round(entry * (1m + (isLong ? 1m : -1m) * (decimal)def.TakeProfitPercent.Value / 100m), 2));
        targets.AddRange(def.TakeProfitTargets.Where(t => t.Price > 0).Select(t => (decimal)t.Price));

        var signal = new TradeSignal
        {
            Symbol = def.Symbol,
            Direction = def.Direction,
            SuggestedEntry = entry,
            SuggestedStop = stop ?? 0m,
            Targets = targets,
            StrategyName = strategy.Title,
            Reason = $"Dry-run vote from the command line on '{strategy.Title}' ({def.EntryConditions.Count} entry conditions); no order will be placed.",
            GeneratedUtc = ctx.UtcNow,
        };
        var threshold = await LlmReads.ThresholdAsync(ctx);
        // A dry run votes even when the gate is off, so a person can see what the panel would say.
        var result = await ctx.Get<LlmVotingService>().VoteOnSignalAsync(signal, candles, credentials with { VotingEnabled = true }, threshold, ctx.CancellationToken);

        var rows = result.Votes.Select(v => new LlmVoteRow(v.PersonaName, v.ModelId, v.Decision.ToString(), v.Confidence, v.Reasoning)).ToList();
        var table = new TextTable { MaxWidth = ctx.Width }.Column("Voter").Column("Vote").Column("Conf", Align.Right).Column("Why", maxWidth: 80);
        foreach (var r in rows) table.Row(r.Voter, r.Decision.ToUpperInvariant(), $"{r.Confidence:0}", r.Reasoning);
        var verdict = rows.Count == 0
            ? "NO VOTES: the panel didn't answer, so the Monitor would block this entry (fail closed)."
            : result.Consensus == VoteDecision.Approve
                ? $"APPROVE at {Fmt.Percent(threshold, 0)}: the LLM gate would let this entry through to the Risk Guardian."
                : $"{result.Consensus.ToString().ToUpperInvariant()}: the Monitor would block this entry at the LLM gate.";
        var head = $"{def.Symbol} {def.Direction} '{strategy.Title}' @ {Fmt.Money(entry)}" +
                   (stop is { } s ? $", stop {Fmt.Money(s)}" : ", no stop") +
                   (targets.Count > 0 ? $", target {Fmt.Money(targets[0])}" : "") +
                   (credentials.VotingEnabled ? "" : "\nNote: voting is off for you and the host, so the live gate is skipped; this is what it would say.");
        var data = new
        {
            strategy = strategy.Id, symbol = def.Symbol, entry, stop, targets, threshold, consensus = result.Consensus.ToString(),
            confidence = result.ConsensusConfidence, gateActive = credentials.VotingEnabled, keySource = credentials.KeySource, votes = rows, dryRun = true,
        };
        return OperationResult.Ok(data, head + "\n\n" + (rows.Count == 0 ? "" : table.Render() + "\n") + verdict + "\nDry run: nothing was placed.");
    }
}
