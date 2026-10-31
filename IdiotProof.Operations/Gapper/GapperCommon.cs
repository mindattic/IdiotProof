using System.Globalization;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Strategies;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Gapper;

/// <summary>
/// The rules every gapper command shares with the web Gapper tab: what counts as a gapper row
/// (its exit brain, <c>PeakGiveback(</c>, which only <see cref="GapperScriptFactory"/> emits), the
/// three-active-gappers cap, one active strategy per symbol, and the queue path (factory →
/// canonical JSON → strategy row → activate → re-check, reverting on a lost race).
/// </summary>
public static class GapperRules
{
    /// <summary>At most this many gappers are active at once (the Gapper tab's cap).</summary>
    public const int MaxActiveGappers = 3;

    /// <summary>The profile a command uses when none is named.</summary>
    public const string DefaultProfileId = "classic-gapper";

    /// <summary>Gapper rows are recognized by the semantic marker only the gapper factory emits.</summary>
    public static bool IsGapper(Strategy s) => s.ScriptText.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase);

    /// <summary>The owner's gapper rows, freshly read from SQL.</summary>
    public static async Task<List<Strategy>> GappersAsync(OperationContext ctx) =>
        (await ctx.Get<StrategyRepository>().GetAllForUserAsync(ctx.RequireOwner(), ctx.CancellationToken)).Where(IsGapper).ToList();

    /// <summary>The authoritative active-gapper count, re-read from SQL (never a cached list).</summary>
    public static async Task<int> CountActiveAsync(OperationContext ctx) => (await GappersAsync(ctx)).Count(s => s.IsActive);

    /// <summary>
    /// The profile named by id (<c>classic-gapper</c>) or display name (<c>"Classic Gapper"</c>),
    /// cloned so dial-ins never touch the catalog; a usage error lists the catalog otherwise.
    /// </summary>
    public static GapperProfile ResolveProfile(OperationContext ctx, string? name)
    {
        var profiles = ctx.Get<GapperProfileService>().GetProfiles();
        var wanted = string.IsNullOrWhiteSpace(name) ? DefaultProfileId : name.Trim();
        var hit = profiles.FirstOrDefault(p => string.Equals(p.Id, wanted, StringComparison.OrdinalIgnoreCase))
               ?? profiles.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
        return hit?.Clone() ?? throw OperationException.Usage(
            $"No gapper profile '{wanted}'. Profiles: {string.Join(", ", profiles.Select(p => p.Id))} (`idiotproof gapper profiles`).");
    }

    /// <summary>
    /// Finds the owner's gapper strategy a person means: an id, id prefix or stamp id resolves
    /// exactly (and must be a gapper); a symbol picks that symbol's gapper (the active one
    /// first). Null when the reference is a symbol with no gapper yet.
    /// </summary>
    public static async Task<Strategy?> FindAsync(OperationContext ctx, string reference)
    {
        var r = (reference ?? "").Trim();
        if (r.Length == 0) throw OperationException.Usage("Name a gapper: its symbol, id, id prefix or stamp id.");
        var gappers = await GappersAsync(ctx);

        if (!LooksLikeSymbol(r) || Guid.TryParse(r, out _))
        {
            var s = await StrategyRefs.ResolveAsync(ctx, r);
            return IsGapper(s) ? s : throw OperationException.Usage($"{s.Symbol} '{s.Title}' is not a gapper (no PeakGiveback exit). Use `idiotproof strategy ...` for it.");
        }

        var forSymbol = gappers.Where(s => s.Symbol.Equals(r, StringComparison.OrdinalIgnoreCase)).ToList();
        if (forSymbol.Count == 0) return null;
        var active = forSymbol.Where(s => s.IsActive).ToList();
        if (active.Count == 1) return active[0];
        if (forSymbol.Count == 1) return forSymbol[0];
        throw OperationException.Usage($"{r.ToUpperInvariant()} has {forSymbol.Count} gappers — name one by id:\n" +
            string.Join("\n", forSymbol.Select(s => $"  {StrategyRefs.ShortGuid(s.Id)}  {(s.IsActive ? "active  " : "inactive")}  {s.Title}")));
    }

    /// <summary>A ticker-shaped reference (1-6 letters, optional .X class suffix) that is not an IP- stamp.</summary>
    public static bool LooksLikeSymbol(string r) =>
        r.Length is >= 1 and <= 8 && !r.StartsWith("IP-", StringComparison.OrdinalIgnoreCase)
        && r.All(c => char.IsLetter(c) || c == '.') && char.IsLetter(r[0]);

    /// <summary>
    /// Builds the gapper for (symbol, profile) the way the Gapper tab does — definition-direct:
    /// canonical JSON from the factory's model, script text as the human view.
    /// </summary>
    public static GapperBuild Compose(string symbol, GapperProfile profile)
    {
        var sym = NormalizeSymbol(symbol);
        try
        {
            var builder = GapperScriptFactory.Compose(sym, profile);
            var def = builder.Build();
            return new GapperBuild(sym, profile, builder.ToScript(), StrategyJson.Serialize(def), def);
        }
        catch (ArgumentException ex) { throw OperationException.Usage(ex.Message); }
    }

    /// <summary>Upper-cases and validates a ticker.</summary>
    public static string NormalizeSymbol(string symbol)
    {
        var sym = (symbol ?? "").Trim().ToUpperInvariant();
        if (!LooksLikeSymbol(sym)) throw OperationException.Usage($"'{symbol}' is not a ticker symbol.");
        return sym;
    }

    /// <summary>The one-line plan the Gapper tab writes as the strategy description.</summary>
    public static string Describe(GapperProfile p, string profileName) =>
        $"Gapper profile '{profileName}': ≥{Num(p.MinGapPercent)}% gap on {Num(p.MinVolumeRatio)}× volume, " +
        $"entry {p.EntryWindowStartEt}–{p.EntryWindowEndEt} ET, stop {Num(p.StopLossPercent)}%, " +
        $"giveback {Num(p.PeakGivebackPercent)}% armed {p.ArmExitAtEt}, flat by {p.SellByEt} ET.";

    /// <summary>Creates the (inactive) strategy row for a composed gapper in <paramref name="mode"/>.</summary>
    public static async Task<Strategy> CreateAsync(OperationContext ctx, GapperBuild build, string profileName, BrokerMode mode, string? titleSuffix = null)
    {
        if (mode == BrokerMode.Live)
            throw OperationException.Usage("New gappers start in paper or sandbox. Route one to live afterwards with `idiotproof strategy mode <id> live` (it asks for a confirmation code).");
        var owner = ctx.RequireOwner();
        var repo = ctx.Get<StrategyRepository>();
        var created = await repo.CreateAsync(owner,
            title: $"{build.Symbol} Gapper — {profileName}{titleSuffix}",
            symbol: build.Symbol, scriptText: build.Script, scriptJson: build.CanonicalJson,
            description: Describe(build.Profile, profileName),
            author: $"{ctx.Caller.ToString().ToLowerInvariant()}-gapper", ct: ctx.CancellationToken);
        await repo.SetBrokerModeAsync([created.Id], mode.ToString(), owner, ctx.CancellationToken);
        created.BrokerMode = mode.ToString();
        return created;
    }

    /// <summary>
    /// Why <paramref name="s"/> may not go active right now (one active strategy per symbol, the
    /// three-gapper cap), or null when it may.
    /// </summary>
    public static async Task<string?> RefuseActivationAsync(OperationContext ctx, string symbol, Guid? excluding = null)
    {
        var owner = ctx.RequireOwner();
        var all = await ctx.Get<StrategyRepository>().GetAllForUserAsync(owner, ctx.CancellationToken);
        if (all.Any(s => s.IsActive && s.Id != excluding && s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            return $"{symbol.ToUpperInvariant()} already has an active strategy — one per symbol. Disarm it first (`idiotproof gapper disarm {symbol.ToUpperInvariant()}`).";
        var active = all.Count(s => s.IsActive && s.Id != excluding && IsGapper(s));
        if (active >= MaxActiveGappers)
            return $"{MaxActiveGappers} gappers are already active (the cap) — disarm one first. `idiotproof gapper caps` shows them.";
        return null;
    }

    /// <summary>
    /// Activates a gapper row under the cap and one-per-symbol rules, then re-checks against SQL
    /// and reverts its own activation when a concurrent arm won the race. A Live row needs a
    /// confirmation code first.
    /// </summary>
    public static async Task<OperationResult> ArmAsync(OperationContext ctx, Strategy s, object? extra = null)
    {
        StrategyRefs.RequireDefinition(s); // never hand the Monitor a quarantined canon
        var mode = OrderIntent.ParseMode(s.BrokerMode);
        if (s.IsActive)
            return OperationResult.Ok(new { s.Id, s.Symbol, s.Title, Mode = mode.ToString(), IsActive = true, Extra = extra },
                $"{s.Symbol} '{s.Title}' is already armed ({mode}).");

        if (await RefuseActivationAsync(ctx, s.Symbol, s.Id) is { } refusal) return OperationResult.Blocked(refusal);

        if (mode == BrokerMode.Live)
        {
            var gate = await LiveGate.RequireAsync(ctx, s, "activate",
                $"ARM gapper {s.Symbol} '{s.Title}' · Live — the Monitor will trade real money when its premarket conditions match.");
            if (gate is not null) return gate;
        }

        var owner = ctx.RequireOwner();
        var repo = ctx.Get<StrategyRepository>();
        var result = await repo.SetActiveAsync(s.Id, true, owner, ctx.CancellationToken);
        if (result == StrategyMutation.NotOwner) return OperationResult.Blocked("That strategy belongs to another user.");
        if (result != StrategyMutation.Ok) return OperationResult.Fail($"Could not arm {s.Symbol}: {result}.");

        // Close the check-then-act race exactly like the Gapper tab: re-verify after the write.
        var all = await repo.GetAllForUserAsync(owner, ctx.CancellationToken);
        if (all.Count(x => x.IsActive && IsGapper(x)) > MaxActiveGappers
            || all.Count(x => x.IsActive && x.Symbol.Equals(s.Symbol, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            await repo.SetActiveAsync(s.Id, false, owner, ctx.CancellationToken);
            return OperationResult.Blocked($"{s.Symbol} lost the race to another arm (cap or duplicate) — left disarmed.");
        }

        var count = all.Count(x => x.IsActive && IsGapper(x));
        return OperationResult.Ok(new { s.Id, s.Symbol, s.Title, Mode = mode.ToString(), IsActive = true, ActiveGappers = count, Cap = MaxActiveGappers, Extra = extra },
            $"Armed {s.Symbol} '{s.Title}' ({mode}) as {StrategyRefs.ShortGuid(s.Id)} — the Monitor picks it up on its next tick. " +
            $"{count}/{MaxActiveGappers} gappers active.");
    }

    internal static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>A composed gapper: the tuned profile, its script view, canon and definition.</summary>
public sealed record GapperBuild(string Symbol, GapperProfile Profile, string Script, string CanonicalJson, StrategyDefinition Definition);

/// <summary>
/// The per-ticker dial-ins every gapper command accepts on top of a catalog profile. Blank means
/// "keep the profile's value"; <c>--max-gap 0</c> / <c>--trail 0</c> turn those optional rails off.
/// </summary>
public abstract record GapperDialArgs
{
    [Option("profile", Description = "Gapper profile id or name to start from (default classic-gapper).", Short = 'p')]
    public string? Profile { get; init; }

    [Option("gap", Description = "Minimum gap over the previous close, percent.")]
    public double? Gap { get; init; }

    [Option("max-gap", Description = "Gap ceiling in percent (0 = no ceiling).")]
    public double? MaxGap { get; init; }

    [Option("volume", Description = "Premarket volume must be at least this multiple of average volume.")]
    public double? Volume { get; init; }

    [Option("min-price", Description = "Lowest price that qualifies.")]
    public double? MinPrice { get; init; }

    [Option("max-price", Description = "Highest price that qualifies.")]
    public double? MaxPrice { get; init; }

    [Option("entry-start", Description = "Entry window start, ET (HH:mm).")]
    public string? EntryStart { get; init; }

    [Option("entry-end", Description = "Entry window end, ET (HH:mm).")]
    public string? EntryEnd { get; init; }

    [Option("stop", Description = "Hard stop below entry, percent.")]
    public double? Stop { get; init; }

    [Option("trail", Description = "Trailing stop off the high, percent (0 = none).")]
    public double? Trail { get; init; }

    [Option("giveback", Description = "Sell once price gives back this percent of the entry-to-peak run.")]
    public double? Giveback { get; init; }

    [Option("arm-exit", Description = "ET time the giveback exit arms (HH:mm).")]
    public string? ArmExit { get; init; }

    [Option("sell-by", Description = "Hard flatten time, ET (HH:mm), before the 9:30 bell.")]
    public string? SellBy { get; init; }

    [Option("notional", Description = "Position size in dollars.")]
    public decimal? Notional { get; init; }

    /// <summary>True when any dial was given (the profile was tuned, not used as-is).</summary>
    public bool AnyDial => Gap is not null || MaxGap is not null || Volume is not null || MinPrice is not null || MaxPrice is not null
        || EntryStart is not null || EntryEnd is not null || Stop is not null || Trail is not null || Giveback is not null
        || ArmExit is not null || SellBy is not null || Notional is not null;

    /// <summary>The named profile with these dial-ins applied, validated.</summary>
    public GapperProfile Tune(OperationContext ctx)
    {
        var p = GapperRules.ResolveProfile(ctx, Profile);
        if (Gap is { } g) p.MinGapPercent = g;
        if (MaxGap is { } mg) p.MaxGapPercent = mg <= 0 ? null : mg;
        if (Volume is { } v) p.MinVolumeRatio = v;
        if (MinPrice is { } lo) p.MinPrice = lo;
        if (MaxPrice is { } hi) p.MaxPrice = hi;
        if (EntryStart is { } es) p.EntryWindowStartEt = es.Trim();
        if (EntryEnd is { } ee) p.EntryWindowEndEt = ee.Trim();
        if (Stop is { } st) p.StopLossPercent = st;
        if (Trail is { } tr) p.TrailingStopPercent = tr <= 0 ? null : tr;
        if (Giveback is { } gb) p.PeakGivebackPercent = gb;
        if (ArmExit is { } ax) p.ArmExitAtEt = ax.Trim();
        if (SellBy is { } sb) p.SellByEt = sb.Trim();
        if (Notional is { } n) p.DefaultNotional = n;
        var problems = p.Validate();
        if (problems.Count > 0) throw OperationException.Usage(string.Join(" ", problems));
        return p;
    }
}
