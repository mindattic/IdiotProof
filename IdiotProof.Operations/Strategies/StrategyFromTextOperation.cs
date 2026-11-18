using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Trading;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Strategies;

public sealed record StrategyFromTextArgs
{
    [Argument(0, Name = "description", Description = "The strategy in plain English (`-` reads stdin).")]
    public string Description { get; init; } = "";

    [Option("ticker", Description = "The symbol to trade (default: the first ticker-looking word, e.g. $NVDA or NVDA).", Short = 't')]
    public string? Ticker { get; init; }

    [Option("mode", Description = "Broker mode for the saved strategy: sandbox (default) or paper.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("title", Description = "Title for the saved strategy (default: its own name, else \"SYM strategy\").", Example = "NVDA VWAP reclaim")]
    public string? Title { get; init; }
}

[Operation("strategy from-text", Summary = "Turn a plain-English description into a strategy (via the LLM), show its stamp, and save it only when you confirm.", Risk = OperationRisk.Write,
    Description = "The first call writes the IdiotScript, validates it and shows the stamp with a confirmation code; nothing is saved. " +
                  "A script that does not validate is repaired: spelling and punctuation fixes that leave a fully valid script are applied directly, anything else goes back to the LLM with its errors " +
                  "for at most `llm.repair.rounds` more calls (default 2). The result's `repair` says what was corrected; a script that never validates is refused with its last errors. " +
                  "Re-run with --confirm <code> to save exactly what was shown (the LLM is not called again). It saves inactive, in Sandbox by default, 1 share when unsized.")]
[Example("idiotproof strategy from-text \"Buy NVDA when it reclaims VWAP with volume, stop 2% under, take profit at 190\"")]
[Example("idiotproof strategy from-text \"long on a 9 EMA reclaim above VWAP, 3% stop\" --ticker AMD --mode paper")]
public sealed class StrategyFromTextOperation : IOperation<StrategyFromTextArgs>
{
    /// <summary>The ConfirmationStore kind for "save this generated strategy".</summary>
    internal const string Kind = "strategy-save";

    private static readonly Regex TickerWord = new(@"(?<![A-Za-z])\$?([A-Z]{1,5})(?![A-Za-z])", RegexOptions.Compiled);

    private static readonly HashSet<string> NotTickers = new(StringComparer.Ordinal)
    {
        "I", "A", "AM", "PM", "ET", "RTH", "VWAP", "EMA", "SMA", "RSI", "ADX", "MACD", "ATR", "HOD", "LOD", "DI",
        "OR", "AND", "IF", "THE", "BUY", "SELL", "LONG", "SHORT", "STOP", "TP", "SL", "R", "PT", "ORB", "IPO", "ETF", "USD", "EOD",
    };

    public async Task<OperationResult> ExecuteAsync(StrategyFromTextArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        StrategyDrafts.RequireStartMode(args.Mode);
        if (!string.IsNullOrWhiteSpace(ctx.ConfirmationCode)) return await SaveConfirmedAsync(ctx, owner);

        var prose = (args.Description ?? "").Trim();
        if (prose.Length == 0) return OperationResult.Usage("Describe the strategy in plain English, e.g. \"buy NVDA on a VWAP reclaim, 2% stop\".");
        var ticker = (args.Ticker ?? GuessTicker(prose))?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ticker))
            return OperationResult.Usage("Which symbol? Pass --ticker SYM (no ticker-looking word in the description).");

        var credentials = await ctx.Get<IClaudeKeyContext>().ResolveAsync(ctx.CancellationToken);
        var policy = await GenerationRepairPolicy.ReadAsync(ctx.Get<SettingsRepository>(), ctx.CancellationToken);
        // The repair loop (IP-T524): every script the model returns is validated in full; a complete
        // deterministic fix is taken without another call, anything else goes back to the model.
        var generated = await ctx.Get<StrategyScriptGenerator>().GenerateAsync(prose, ticker, credentials,
            script => GeneratedStrategyCheck.Inspect(script, ticker), policy.MaxRounds, ctx.CancellationToken);
        var repair = generated.Repair;
        if (!generated.Success || generated.Value is null)
        {
            var why = generated.Error ?? "The LLM didn't produce a strategy.";
            if (generated.Diagnostics.Count == 0) return OperationResult.Fail(why, new { Saved = false, Repair = repair });
            return OperationResult.Fail(
                $"{why} Nothing saved. Reword the description, or fix the script and use `strategy create --script`.\n" +
                string.Join("\n", generated.Diagnostics.Select(d => "  - " + d)) +
                (repair.Steps.Count == 0 ? "" : "\n" + repair.Render()) + "\n\n" + generated.Script,
                new { Saved = false, generated.Script, Errors = generated.Diagnostics, Repair = repair });
        }

        var def = generated.Value.Definition;
        var v = generated.Value.Validation;
        var applied = generated.Value.Applied.ToList();
        if (!string.Equals(def.Symbol, ticker, StringComparison.OrdinalIgnoreCase))
            applied.Add($"note: the script trades {def.Symbol}, not {ticker}");
        var stamp = def.ToStamp(new StampOptions { IncludePayload = false });
        var defaults = applied.Count == 0 ? "" : $"Defaults applied: {string.Join(", ", applied)}.\n";
        var repaired = repair.Steps.Count == 0 ? "" : repair.Render() + "\n";

        var title = StrategyDrafts.TitleFor(def, args.Title);
        var payload = JsonSerializer.Serialize(new { canon = v.Canon, title, mode = args.Mode.ToString(), prose });
        var pending = await ctx.Get<ConfirmationStore>().IssueAsync(owner, Kind, payload, Hash(payload),
            $"SAVE {def.Symbol} '{title}' ({v.StampId}) · {args.Mode}, inactive", ctx.CancellationToken);

        var preview = stamp + "\n" + v.Script + "\n\n" + defaults + repaired + v.Render(ctx.Width) +
                      $"\n\nNothing saved yet. To save it as {args.Mode}, inactive: idiotproof strategy from-text \"...\" --confirm {pending.Code}";
        return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc,
            new { Saved = false, Stamp = v.StampId, def.Symbol, Title = title, Mode = args.Mode.ToString(), v.Script, v.Findings, v.Risk, Repair = repair });
    }

    private static async Task<OperationResult> SaveConfirmedAsync(OperationContext ctx, Guid owner)
    {
        var redemption = await ctx.Get<ConfirmationStore>().RedeemAsync(ctx.ConfirmationCode!, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == Kind ? Hash(p.PayloadJson) : "mismatch", ctx.CancellationToken);
        if (!redemption.Succeeded) return OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });

        using var doc = JsonDocument.Parse(redemption.Confirmation!.PayloadJson);
        var root = doc.RootElement;
        var def = StrategyJson.Deserialize(root.GetProperty("canon").GetString()!);
        var mode = Enum.Parse<BrokerMode>(root.GetProperty("mode").GetString()!);
        var title = root.GetProperty("title").GetString();
        var prose = root.GetProperty("prose").GetString();

        var created = await StrategyDrafts.SaveAsync(ctx, def, title, mode, "from-text", description: prose);
        var shortId = StrategyRefs.ShortGuid(created.Id);
        return OperationResult.Ok(new { Saved = true, created.Id, Short = shortId, Stamp = def.ShortId(), def.Symbol, created.Title, Mode = mode.ToString() },
            $"Saved {def.Symbol} '{created.Title}' as {shortId} ({def.ShortId()}), inactive, {mode}.\nActivate it with `idiotproof strategy activate {shortId}`.");
    }

    /// <summary>The first ticker-looking word: <c>$NVDA</c>, or an all-caps word that isn't trading jargon.</summary>
    internal static string? GuessTicker(string prose)
    {
        foreach (Match m in TickerWord.Matches(prose))
        {
            var word = m.Groups[1].Value;
            if (m.Value.StartsWith('$') || !NotTickers.Contains(word)) return word;
        }
        return null;
    }

    private static string Hash(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
