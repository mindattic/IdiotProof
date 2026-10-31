using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Services;
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
/// Exercise and do-not-exercise are instructions on a position, not orders, so they skip the order
/// gateway but keep its rules: Alpaca accounts only, Live waits for a confirmation code bound to
/// (owner, contract, action, mode, contracts), and every attempt — done, refused or failed — leaves
/// an audit row.
/// </summary>
public static class OptionsExerciseFlow
{
    public const string ConfirmationKind = "options-exercise";

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
        var payload = JsonSerializer.Serialize(new { owner, symbol, action = verb, mode = args.Mode.ToString(), qty });
        var data = new { Symbol = symbol, Action = verb, Mode = args.Mode.ToString(), Contracts = qty, Contract = contract?.DisplayName };

        if (args.Mode == BrokerMode.Live)
        {
            var gate = await LiveGateAsync(ctx, owner, payload, preview, data);
            if (gate is not null)
            {
                await AuditAsync(ctx, owner, gate.Status == OperationStatus.ConfirmationRequired ? "ConfirmationRequired" : "Blocked",
                    preview, payload, args.Note, gate.Message);
                return gate;
            }
        }

        try
        {
            await api.SendAsync(HttpMethod.Post, $"/v2/positions/{Uri.EscapeDataString(symbol)}/{verb}", ct: ctx.CancellationToken);
        }
        catch (AlpacaApiException ex)
        {
            await AuditAsync(ctx, owner, "Rejected", preview, payload, args.Note, ex.AlpacaMessage);
            return OperationResult.Fail($"Alpaca {args.Mode} refused {verb} on {symbol}: {ex.AlpacaMessage}", data);
        }

        var done = action == ExerciseAction.Exercise
            ? $"Exercise instruction sent to Alpaca {args.Mode} for {symbol}."
            : $"{symbol} on {args.Mode} is marked do-not-exercise: it will not be auto-exercised at expiry.";
        await AuditAsync(ctx, owner, "Placed", preview, payload, args.Note, done);
        return OperationResult.Ok(data, preview + "\n\n" + done);
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

    /// <summary>First Live call issues a code; the same call with that code proceeds once. Null = proceed.</summary>
    private static async Task<OperationResult?> LiveGateAsync(OperationContext ctx, Guid owner, string payload, string preview, object data)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var pending = await store.IssueAsync(owner, ConfirmationKind, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, data) with { Human = preview };
        }
        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationKind && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }

    private static async Task AuditAsync(OperationContext ctx, Guid owner, string status, string preview, string payload, string? note, string? message)
    {
        var data = JsonSerializer.Serialize(new
        {
            status,
            caller = ctx.Caller.ToString().ToLowerInvariant(),
            request = JsonDocument.Parse(payload).RootElement,
            note,
            message,
        });
        var category = status is "Placed" or "Rejected" ? "order" : "risk";
        await ctx.Get<AuditLogRepository>().LogAsync(category, $"{status}: {preview.Split('\n')[0]} — {message}", owner, data, ctx.CancellationToken);
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
