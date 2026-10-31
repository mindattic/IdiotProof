using System.Globalization;
using System.Net;
using System.Text;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Options;

/// <summary>What to tell the OCC about a held contract.</summary>
public enum ExerciseAction
{
    /// <summary>Exercise now (POST /v2/positions/{occ}/exercise).</summary>
    Exercise,
    /// <summary>Opt out of automatic exercise at expiry (POST /v2/positions/{occ}/do-not-exercise).</summary>
    DoNotExercise,
}

/// <summary>Arguments for exercise / do-not-exercise on a held contract.</summary>
public sealed record OptionsExerciseArgs
{
    [Argument(0, Name = "occ", Description = "OCC contract symbol (or Alpaca contract id) of a long position you hold.")]
    public string Occ { get; init; } = "";

    [Option("mode", Description = "Which Alpaca account: paper (default) or live (needs confirmation). Sandbox can't exercise.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why — kept in the audit trail.")]
    public string? Note { get; init; }
}

/// <summary>
/// Exercise and do-not-exercise are instructions on a position, not orders, but they move money
/// all the same, so they go through the TransactionGateway as a <see cref="BrokerInstruction"/>
/// (IP-LAW-12): Alpaca accounts only, Live waits for a confirmation code bound to (owner,
/// contract, action, mode, contracts), and every attempt — done, refused or failed — leaves an
/// audit row linked to the command ledger. This flow only reads the position for the preview.
/// </summary>
public static class OptionsExerciseFlow
{
    public static async Task<OperationResult> RunAsync(ExerciseAction action, OptionsExerciseArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var id = args.Occ.Trim();
        if (id.Length == 0) throw OperationException.Usage("Name the contract, e.g. `idiotproof options exercise AAPL261219C00150000`.");
        var contract = OptionContract.ParseOcc(id);
        var symbol = contract?.OccSymbol ?? id; // Alpaca also accepts the contract id
        var verb = action == ExerciseAction.Exercise ? "exercise" : "do-not-exercise";
        if (args.Mode == BrokerMode.Sandbox)
            return OperationResult.Usage($"Sandbox is IdiotProof's simulator and has no OCC to {verb} with; use --mode paper or --mode live.");

        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, args.Mode, ctx.CancellationToken);

        // The position is what gets exercised; read it so the preview says what will happen.
        decimal qty;
        try
        {
            var position = await api.GetAsync($"/v2/positions/{Uri.EscapeDataString(symbol)}", ct: ctx.CancellationToken);
            qty = decimal.TryParse(position.TryGetProperty("qty", out var q) ? q.GetString() : null,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
        }
        catch (AlpacaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return OperationResult.Blocked($"No open {symbol} position on {args.Mode} to {verb}.");
        }
        if (qty <= 0)
            return OperationResult.Blocked($"You are short {symbol} on {args.Mode}; only the holder of a long contract can {verb} it.");

        var preview = Preview(action, symbol, contract, qty, args.Mode);
        var data = new { Symbol = symbol, Action = verb, Mode = args.Mode.ToString(), Contracts = qty, Contract = contract?.DisplayName };
        var instruction = new BrokerInstruction
        {
            OwnerUserId = owner,
            Mode = args.Mode,
            Action = verb,
            Method = "POST",
            Path = $"/v2/positions/{Uri.EscapeDataString(symbol)}/{verb}",
            Quantity = qty,
            Preview = preview,
            Note = args.Note,
        };

        // The gateway sends it (IP-LAW-12): audit, ledger link, and a Live code bound to this exact request.
        var outcome = await ctx.Get<TransactionGateway>().SubmitInstructionAsync(instruction,
            ctx.Caller.ToString().ToLowerInvariant(), ctx.ConfirmationCode, ctx.CancellationToken);
        return outcome.Status switch
        {
            GatewayStatus.Placed => OperationResult.Ok(data, preview + "\n\n" + (action == ExerciseAction.Exercise
                ? $"Exercise instruction sent to Alpaca {args.Mode} for {symbol}."
                : $"{symbol} on {args.Mode} is marked do-not-exercise: it will not be auto-exercised at expiry.")),
            GatewayStatus.ConfirmationRequired => OperationResult.NeedsConfirmation(
                outcome.ConfirmationCode!, preview, outcome.ConfirmationExpiresUtc!.Value, data) with { Human = preview },
            GatewayStatus.Rejected => OperationResult.Fail($"Alpaca {args.Mode} refused {verb} on {symbol}: {outcome.BrokerMessage ?? outcome.Message}", data),
            _ => OperationResult.Blocked(outcome.Message, data),
        };
    }

    /// <summary>"EXERCISE 2 AAPL $150 Call · Dec 19 2026 · Live — buys 200 AAPL at $150.00 ($30,000.00)".</summary>
    internal static string Preview(ExerciseAction action, string symbol, OptionContract? contract, decimal qty, BrokerMode mode)
    {
        var count = qty.ToString("0.##", CultureInfo.InvariantCulture);
        var what = contract is null ? symbol : $"{contract.DisplayName} ({symbol})";
        var sb = new StringBuilder();
        sb.AppendLine($"{(action == ExerciseAction.Exercise ? "EXERCISE" : "DO NOT EXERCISE")} {count} {what} · {mode}");
        if (contract is not null)
        {
            var shares = qty * contract.Multiplier;
            var cash = Fmt.Money(shares * contract.Strike);
            var strike = Fmt.Money(contract.Strike);
            sb.AppendLine(action == ExerciseAction.Exercise
                ? (contract.Right == OptionRight.Call
                    ? $"  delivers  buys {shares:0} {contract.UnderlyingSymbol} at {strike} ({cash} cash out)"
                    : $"  delivers  sells {shares:0} {contract.UnderlyingSymbol} at {strike} ({cash} cash in)")
                : "  effect    an in-the-money contract expires instead of auto-exercising");
        }
        return sb.ToString().TrimEnd();
    }
}

[Operation("options exercise", Summary = "Exercise a long option you hold (Alpaca paper or live; live needs a confirmation code).", Risk = OperationRisk.Money)]
[Example("idiotproof options exercise AAPL261219C00150000", Description = "Paper: exercise now.")]
[Example("idiotproof options exercise AAPL261219C00150000 --mode live", Description = "Prints a confirmation code first.")]
public sealed class OptionsExerciseOperation : IOperation<OptionsExerciseArgs>
{
    public Task<OperationResult> ExecuteAsync(OptionsExerciseArgs args, OperationContext ctx) =>
        OptionsExerciseFlow.RunAsync(ExerciseAction.Exercise, args, ctx);
}

[Operation("options do-not-exercise", Summary = "Opt a held contract out of automatic exercise at expiry (paper or live; live needs a code).", Risk = OperationRisk.Money)]
[Example("idiotproof options do-not-exercise AAPL261219C00150000 --mode paper")]
public sealed class OptionsDoNotExerciseOperation : IOperation<OptionsExerciseArgs>
{
    public Task<OperationResult> ExecuteAsync(OptionsExerciseArgs args, OperationContext ctx) =>
        OptionsExerciseFlow.RunAsync(ExerciseAction.DoNotExercise, args, ctx);
}
