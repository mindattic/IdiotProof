using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

/// <summary>
/// Resolves the many ways a person names a strategy — full id, an id prefix, its stamp id
/// (<c>IP-XXXX-XXXX</c>) or its symbol — to exactly one row the owner owns, or a usage error
/// that lists the candidates.
/// </summary>
public static class StrategyRefs
{
    public static async Task<Strategy> ResolveAsync(OperationContext ctx, string reference)
    {
        var owner = ctx.RequireOwner();
        var all = await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken);
        var r = (reference ?? "").Trim();
        if (r.Length == 0) throw OperationException.Usage("Name a strategy: its id, an id prefix, its stamp id (IP-XXXX-XXXX) or its symbol.");

        if (Guid.TryParse(r, out var id))
            return all.FirstOrDefault(s => s.Id == id) ?? throw OperationException.Usage($"No strategy of yours has id {id}.");

        List<Strategy> matches;
        if (r.StartsWith("IP-", StringComparison.OrdinalIgnoreCase))
            matches = all.Where(s => string.Equals(StampId(s), r, StringComparison.OrdinalIgnoreCase)).ToList();
        else
        {
            var compact = r.Replace("-", "");
            matches = compact.Length >= 4 && compact.All(Uri.IsHexDigit)
                ? all.Where(s => s.Id.ToString("N").StartsWith(compact, StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            if (matches.Count == 0)
                matches = all.Where(s => string.Equals(s.Symbol, r, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw OperationException.Usage($"No strategy of yours matches '{r}'. `idiotproof strategy list` shows them."),
            _ => throw OperationException.Usage($"'{r}' matches {matches.Count} strategies — use an id:\n" +
                string.Join("\n", matches.Select(m => $"  {ShortGuid(m.Id)}  {m.Symbol,-6} {m.Title}"))),
        };
    }

    public static StrategyLoadResult Load(Strategy s) => StrategyLoader.Load(s.ScriptJson, s.ScriptText);

    /// <summary>The stamp id of the strategy's canon, or "(quarantined)" when it can't be read.</summary>
    public static string StampId(Strategy s) => Load(s).Definition is { } def ? def.ShortId() : "(quarantined)";

    public static string ShortGuid(Guid id) => id.ToString("N")[..8];

    public static StrategyDefinition RequireDefinition(Strategy s)
    {
        var load = Load(s);
        return load.Definition ?? throw OperationException.Failed(
            $"{s.Symbol} '{s.Title}' is quarantined — its canon can't be read: {load.CanonicalError}");
    }
}

public sealed record StrategyRefArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id (IP-XXXX-XXXX) or symbol.")]
    public string Strategy { get; init; } = "";
}

public sealed record StrategyListArgs
{
    [Option("active", Description = "Only strategies the Monitor is evaluating.", Short = 'a')]
    public bool Active { get; init; }

    [Option("symbol", Description = "Only this symbol.")]
    public string? Symbol { get; init; }

    [Option("mode", Description = "Only this broker mode (sandbox, paper, live).")]
    public BrokerMode? Mode { get; init; }

    [Option("deleted", Description = "Include soft-deleted strategies (hidden by default).")]
    public bool Deleted { get; init; }
}

[Operation("strategy list", Summary = "List your strategies with mode, position and live condition progress.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy list")]
[Example("idiotproof strategy list --active --mode live")]
public sealed class StrategyListOperation : IOperation<StrategyListArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyListArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var rows = (await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken))
            .Where(s => !args.Active || s.IsActive)
            .Where(s => args.Deleted || !StrategyArchive.IsDeleted(s))
            .Where(s => args.Symbol is null || s.Symbol.Equals(args.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(s => args.Mode is null || OrderIntent.ParseMode(s.BrokerMode) == args.Mode)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Symbol).ThenBy(s => s.Title)
            .ToList();
        var progress = await ctx.Get<ConditionProgressRepository>().GetForStrategyIdsAsync(rows.Select(r => r.Id), ctx.CancellationToken);

        var items = rows.Select(s => new
        {
            Id = s.Id,
            Short = StrategyRefs.ShortGuid(s.Id),
            Stamp = StrategyRefs.StampId(s),
            s.Symbol,
            s.Title,
            Mode = OrderIntent.ParseMode(s.BrokerMode).ToString(),
            s.IsActive,
            Position = s.PositionQty,
            Progress = progress.TryGetValue(s.Id, out var p) ? $"{p.PassedCount}/{p.TotalCount}" : null,
            Waiting = progress.TryGetValue(s.Id, out var q) ? q.FirstFailingVerb : null,
            s.LastFiredUtc,
        }).ToList();

        if (items.Count == 0) return OperationResult.Ok(items, "No strategies match. Create one with `idiotproof strategy from-text \"...\"` or import a stamp.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("stamp").Column("sym").Column("title", maxWidth: 34).Column("mode")
            .Column("on").Column("pos", Align.Right).Column("cond", Align.Right).Column("waiting on", maxWidth: 24);
        foreach (var i in items)
            table.Row(i.Short, i.Stamp, i.Symbol, i.Title, i.Mode, i.IsActive ? "●" : "·",
                i.Position == 0 ? "" : i.Position.ToString(), i.Progress ?? "", i.Waiting ?? "");
        var active = items.Count(i => i.IsActive);
        return OperationResult.Ok(items, table.Render() + $"\n{items.Count} strategies, {active} active.");
    }
}

[Operation("strategy show", Summary = "Everything about one strategy: settings, script, position, progress.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy show NVDA")]
[Example("idiotproof strategy show IP-P4AY-UXZU")]
public sealed class StrategyShowOperation : IOperation<StrategyRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyRefArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var load = StrategyRefs.Load(s);
        var progress = await ctx.Get<ConditionProgressRepository>().GetAsync(s.Id, ctx.CancellationToken);

        var kv = new KeyValueBlock()
            .Add("id", s.Id.ToString())
            .Add("stamp", load.Definition?.ShortId() ?? "(quarantined)")
            .Add("title", s.Title)
            .Add("symbol", s.Symbol)
            .Add("mode", OrderIntent.ParseMode(s.BrokerMode).ToString())
            .Add("active", s.IsActive ? "yes — the Monitor evaluates it every tick" : "no")
            .Add("position", s.PositionQty == 0 ? "flat" : $"{s.PositionQty} @ {Fmt.Money(s.LastEntryPrice ?? 0)} since {Fmt.Et(s.EntryFilledUtc)}")
            .Add("conditions", progress is null ? "(not evaluated yet)" : $"{progress.PassedCount}/{progress.TotalCount} passing; waiting on {progress.FirstFailingVerb ?? "nothing"} ({Fmt.Et(progress.EvaluatedUtc)})")
            .Add("fired", s.FireCount == 0 ? "never" : $"{s.FireCount}× (last {Fmt.Et(s.LastFiredUtc)})")
            .Add("last exit", s.LastExitedUtc is null ? "—" : $"{Fmt.Money(s.LastExitPrice ?? 0)} — {s.LastExitReason} ({Fmt.Et(s.LastExitedUtc)})")
            .Add("author", s.Author ?? "—")
            .Add("updated", Fmt.Et(s.UpdatedUtc));
        if (load.CanonicalError is not null) kv.Add("QUARANTINED", load.CanonicalError);

        var script = load.Definition?.ToScript() ?? s.ScriptText;
        return OperationResult.Ok(new
        {
            s.Id, Stamp = load.Definition?.ShortId(), s.Title, s.Symbol, Mode = OrderIntent.ParseMode(s.BrokerMode).ToString(),
            s.IsActive, s.PositionQty, s.LastEntryPrice, s.EntryFilledUtc, s.FireCount, s.LastFiredUtc,
            Progress = progress is null ? null : new { progress.PassedCount, progress.TotalCount, progress.FirstFailingVerb, progress.EvaluatedUtc },
            Quarantined = load.CanonicalError, Script = script,
        }, kv.Render() + "\n\n" + script);
    }
}

public sealed record StrategyStampArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("unicode", Description = "Box-drawing characters instead of plain ASCII.", Short = 'u')]
    public bool Unicode { get; init; }

    [Option("qr", Description = "Add a terminal QR code of the payload.")]
    public bool Qr { get; init; }

    [Option("compact", Description = "Leave out the armored payload block (picture only).")]
    public bool Compact { get; init; }
}

[Operation("strategy stamp", Summary = "Print the strategy's ASCII stamp: header, phase flow, exit ladder, fingerprint and a paste-back payload.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy stamp NVDA")]
[Example("idiotproof strategy stamp IP-P4AY-UXZU --unicode --qr")]
public sealed class StrategyStampOperation : IOperation<StrategyStampArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyStampArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var def = StrategyRefs.RequireDefinition(s);
        var stamp = def.ToStamp(new StampOptions { Unicode = args.Unicode, Qr = args.Qr, IncludePayload = !args.Compact });
        return OperationResult.Ok(new { s.Id, Stamp = def.ShortId(), Text = stamp, Armor = def.ToArmor() }, stamp);
    }
}

public enum StrategyExportFormat { Stamp, Armor, Json, Script }

public sealed record StrategyExportArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Option("format", Description = "stamp (default), armor (payload only), json (canonical) or script (IdiotScript).", Short = 'f')]
    public StrategyExportFormat Format { get; init; } = StrategyExportFormat.Stamp;
}

[Operation("strategy export", Summary = "Export a strategy as a stamp, armored payload, canonical JSON or IdiotScript.", Risk = OperationRisk.Read)]
[Example("idiotproof strategy export NVDA --format json")]
public sealed class StrategyExportOperation : IOperation<StrategyExportArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyExportArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var def = StrategyRefs.RequireDefinition(s);
        var text = args.Format switch
        {
            StrategyExportFormat.Armor => def.ToArmor(),
            StrategyExportFormat.Json => StrategyJson.Serialize(def),
            StrategyExportFormat.Script => def.ToScript(),
            _ => def.ToStamp(),
        };
        return OperationResult.Ok(new { s.Id, Stamp = def.ShortId(), Format = args.Format.ToString().ToLowerInvariant(), Text = text }, text);
    }
}

public sealed record StrategyImportArgs
{
    [Argument(0, Name = "source", Description = "A pasted stamp/armor/IPS1 payload, canonical JSON, or IdiotScript text (`-` reads stdin).")]
    public string Source { get; init; } = "";

    [Option("title", Description = "Title for the new strategy (default: the strategy's own name or symbol).")]
    public string? Title { get; init; }

    [Option("mode", Description = "Broker mode for the new strategy: sandbox (default) or paper. Live is a separate, confirmed step.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;
}

[Operation("strategy import", Summary = "Create a strategy from a stamp, canonical JSON or IdiotScript. It starts inactive in Sandbox.", Risk = OperationRisk.Write)]
[Example("idiotproof strategy import -", Description = "Reads the pasted stamp from stdin, e.g. `type gapper-stamp.txt | idiotproof strategy import -`.")]
[Example("idiotproof strategy import \"Ticker(\\\"NVDA\\\").IsAboveVwap().StopLoss(2)\" --mode paper")]
public sealed class StrategyImportOperation : IOperation<StrategyImportArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyImportArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args.Mode == BrokerMode.Live)
            throw OperationException.Usage("Imports start in sandbox or paper. Go live with `idiotproof strategy mode <id> live` (it asks for confirmation).");

        var (def, source) = Parse(args.Source);
        var canon = StrategyJson.Serialize(def);
        var script = def.ToScript();

        // The text view must parse back to the same strategy, or an edit in the web editor would
        // silently change it; say so instead of hiding it.
        string? warning = null;
        try
        {
            var reparsed = ScriptParser.ParseScript(script);
            if (reparsed is null || reparsed.ShortId() != def.ShortId())
                warning = "The IdiotScript view doesn't round-trip exactly; edit this one through the CLI (canon), not the web script editor.";
        }
        catch (Exception ex) { warning = "The IdiotScript view doesn't parse back (" + ex.Message + "); edit through the CLI."; }

        var repo = ctx.Get<StrategyRepository>();
        var title = args.Title ?? def.Name ?? $"{def.Symbol} (imported {def.ShortId()})";
        var created = await repo.CreateAsync(owner, title, def.Symbol, script, scriptJson: canon,
            author: $"{ctx.Caller.ToString().ToLowerInvariant()}-import", ct: ctx.CancellationToken);
        await repo.SetBrokerModeAsync([created.Id], args.Mode.ToString(), owner, ctx.CancellationToken);

        var human = $"Imported {def.Symbol} '{title}' from {source} as {StrategyRefs.ShortGuid(created.Id)} ({def.ShortId()}), " +
                    $"inactive, {args.Mode}.\nActivate it with `idiotproof strategy activate {StrategyRefs.ShortGuid(created.Id)}`."
                    + (warning is null ? "" : "\nNote: " + warning);
        return OperationResult.Ok(new { created.Id, Stamp = def.ShortId(), def.Symbol, Title = title, Mode = args.Mode.ToString(), Source = source, Warning = warning }, human);
    }

    internal static (StrategyDefinition Definition, string Source) Parse(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) throw OperationException.Usage("Nothing to import — paste a stamp, JSON or IdiotScript (or pass `-` and pipe it in).");
        try
        {
            if (t.Contains("BEGIN IDIOTPROOF STRATEGY", StringComparison.Ordinal) || t.StartsWith("IPS1:", StringComparison.Ordinal))
                return (StrategyStamp.Decode(t), "stamp");
            if (t.StartsWith('{'))
                return (StrategyJson.Deserialize(t), "canonical JSON");
            return (ScriptParser.ParseScript(t) ?? throw OperationException.Usage("That IdiotScript didn't parse."), "IdiotScript");
        }
        catch (OperationException) { throw; }
        catch (Exception ex)
        {
            throw OperationException.Usage($"Can't import that: {ex.Message}");
        }
    }
}

[Operation("strategy activate", Summary = "Hand a strategy to the Monitor. A Live strategy needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof strategy activate NVDA")]
public sealed class StrategyActivateOperation : IOperation<StrategyRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyRefArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        StrategyRefs.RequireDefinition(s); // never hand the Monitor a quarantined canon
        if (s.IsActive) return OperationResult.Ok(new { s.Id, s.IsActive }, $"{s.Symbol} '{s.Title}' is already active.");

        if (OrderIntent.ParseMode(s.BrokerMode) == BrokerMode.Live)
        {
            var gate = await LiveGate.RequireAsync(ctx, s, "activate", $"ACTIVATE {s.Symbol} '{s.Title}' · Live — the Monitor will trade real money when its conditions match.");
            if (gate is not null) return gate;
        }
        return await SetActiveAsync(ctx, s, true);
    }

    internal static async Task<OperationResult> SetActiveAsync(OperationContext ctx, Strategy s, bool active)
    {
        var result = await ctx.Get<StrategyRepository>().SetActiveAsync(s.Id, active, ctx.RequireOwner(), ctx.CancellationToken);
        return result switch
        {
            StrategyMutation.Ok => OperationResult.Ok(new { s.Id, IsActive = active },
                active ? $"{s.Symbol} '{s.Title}' is active — the Monitor picks it up on its next tick ({OrderIntent.ParseMode(s.BrokerMode)})."
                       : $"{s.Symbol} '{s.Title}' is inactive."),
            StrategyMutation.PositionOpen => OperationResult.Blocked(
                $"{s.Symbol} holds {s.PositionQty} shares — deactivating would orphan the exit. Flatten first (`idiotproof trade sell {s.Symbol} {s.PositionQty} --mode {OrderIntent.ParseMode(s.BrokerMode).ToString().ToLowerInvariant()}`)."),
            StrategyMutation.NotOwner => OperationResult.Blocked("That strategy belongs to another user."),
            _ => OperationResult.Fail("Strategy not found."),
        };
    }
}

[Operation("strategy deactivate", Summary = "Stop the Monitor evaluating a strategy (refused while it holds a position).", Risk = OperationRisk.Write)]
[Example("idiotproof strategy deactivate NVDA")]
public sealed class StrategyDeactivateOperation : IOperation<StrategyRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyRefArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        return s.IsActive
            ? await StrategyActivateOperation.SetActiveAsync(ctx, s, false)
            : OperationResult.Ok(new { s.Id, s.IsActive }, $"{s.Symbol} '{s.Title}' is already inactive.");
    }
}

public sealed record StrategyModeArgs
{
    [Argument(0, Name = "strategy", Description = "Id, id prefix, stamp id or symbol.")]
    public string Strategy { get; init; } = "";

    [Argument(1, Name = "mode", Description = "sandbox, paper or live (live needs a confirmation code).")]
    public BrokerMode Mode { get; init; }
}

[Operation("strategy mode", Summary = "Route a strategy to sandbox, paper or live. Switching to live needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof strategy mode NVDA paper")]
[Example("idiotproof strategy mode NVDA live")]
public sealed class StrategyModeOperation : IOperation<StrategyModeArgs>
{
    public async Task<OperationResult> ExecuteAsync(StrategyModeArgs args, OperationContext ctx)
    {
        var s = await StrategyRefs.ResolveAsync(ctx, args.Strategy);
        var current = OrderIntent.ParseMode(s.BrokerMode);
        if (current == args.Mode) return OperationResult.Ok(new { s.Id, Mode = current.ToString() }, $"{s.Symbol} is already {current}.");
        if (s.PositionQty > 0)
            return OperationResult.Blocked($"{s.Symbol} holds {s.PositionQty} shares in {current}; changing the route would strand that position. Flatten first.");

        if (args.Mode == BrokerMode.Live)
        {
            var gate = await LiveGate.RequireAsync(ctx, s, "mode-live",
                $"ROUTE {s.Symbol} '{s.Title}' to LIVE" + (s.IsActive ? " — it is active, so the Monitor trades real money on its next match." : "."));
            if (gate is not null) return gate;
        }

        await ctx.Get<StrategyRepository>().SetBrokerModeAsync([s.Id], args.Mode.ToString(), ctx.RequireOwner(), ctx.CancellationToken);
        return OperationResult.Ok(new { s.Id, Mode = args.Mode.ToString() }, $"{s.Symbol} '{s.Title}' now routes to {args.Mode} (was {current}).");
    }
}

/// <summary>
/// The Live gate for strategy changes: the first call issues a code bound to (strategy, action);
/// the same call with that code proceeds — once, for that owner, within the code's lifetime.
/// Returns null when the caller may proceed, else the result to return.
/// </summary>
internal static class LiveGate
{
    public static async Task<OperationResult?> RequireAsync(OperationContext ctx, Strategy s, string action, string preview)
    {
        var owner = ctx.RequireOwner();
        var payload = JsonSerializer.Serialize(new { strategy_id = s.Id, action });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();

        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var pending = await store.IssueAsync(owner, ConfirmationStore.KindActivateLive, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, new { s.Id, action });
        }

        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationStore.KindActivateLive && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }
}
