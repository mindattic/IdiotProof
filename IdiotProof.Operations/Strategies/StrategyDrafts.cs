using System.Globalization;
using System.Text.RegularExpressions;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Trading;
using IdiotProof.Models;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Strategies;

/// <summary>How serious one validation finding is: an error stops a save, a warning is shown, a note informs.</summary>
public enum FindingLevel { Error, Warning, Note }

/// <summary>One thing <see cref="StrategyDrafts.Validate"/> noticed about a strategy.</summary>
public sealed record StrategyFinding(FindingLevel Level, string Rule, string Message);

/// <summary>
/// The full verdict on a candidate strategy: the canon it would store, its stamp id, whether the
/// canon and the IdiotScript view both read back to the same strategy, the findings, and a
/// plain-English risk preview.
/// </summary>
public sealed record StrategyValidation(
    string StampId,
    string Canon,
    string Script,
    bool CanonRoundTrips,
    bool ScriptRoundTrips,
    IReadOnlyList<StrategyFinding> Findings,
    IReadOnlyList<string> Risk)
{
    public bool HasErrors => Findings.Any(f => f.Level == FindingLevel.Error);

    /// <summary>The findings as an aligned table followed by the risk preview.</summary>
    public string Render(int? width = null)
    {
        var lines = new List<string>();
        if (Findings.Count == 0) lines.Add("No problems found.");
        else
        {
            var table = new Rendering.TextTable { MaxWidth = width }.Column("level").Column("rule").Column("finding");
            foreach (var f in Findings) table.Row(f.Level.ToString().ToLowerInvariant(), f.Rule, f.Message);
            lines.Add(table.Render());
        }
        if (Risk.Count > 0)
        {
            lines.Add("");
            lines.Add("Risk preview:");
            lines.AddRange(Risk.Select(r => "  " + r));
        }
        return string.Join("\n", lines);
    }
}

/// <summary>
/// The shared path every "make a new strategy" operation takes (create, from-text, from-watchlist,
/// clone): read the source into a <see cref="StrategyDefinition"/>, apply the house defaults
/// (1 share when no size is given), validate it, and save it inactive in a non-Live mode through
/// <see cref="StrategyRepository"/> with its canon (IP-LAW-8).
/// </summary>
public static class StrategyDrafts
{
    private static readonly Regex SymbolShape = new("^[A-Z][A-Z0-9.]{0,9}$", RegexOptions.Compiled);

    /// <summary>Reads IdiotScript text; a usage error when it doesn't parse.</summary>
    public static StrategyDefinition FromScript(string script)
    {
        try
        {
            return ScriptParser.ParseScript(script ?? "")
                ?? throw OperationException.Usage("That IdiotScript didn't parse. Start it with Stock.Ticker(\"SYM\") (see `idiotproof strategy samples`).");
        }
        catch (OperationException) { throw; }
        catch (Exception ex) { throw OperationException.Usage($"That IdiotScript didn't parse: {ex.Message}"); }
    }

    /// <summary>Reads canonical JSON strictly (the same reader the Monitor uses); a usage error when it is refused.</summary>
    public static StrategyDefinition FromJson(string json)
    {
        try { return StrategyJson.Deserialize(json ?? ""); }
        catch (Exception ex) { throw OperationException.Usage($"That canonical JSON was refused: {ex.Message}"); }
    }

    /// <summary>A deep copy through the canon, so edits to the copy never touch the original.</summary>
    public static StrategyDefinition Copy(StrategyDefinition def) => StrategyJson.Deserialize(StrategyJson.Serialize(def));

    /// <summary>
    /// House defaults for a NEW strategy: a strategy with no size buys 1 share (never a notional
    /// amount), and a missing symbol takes <paramref name="symbol"/>. Returns what was applied.
    /// </summary>
    public static List<string> ApplyDefaults(StrategyDefinition def, string? symbol = null)
    {
        var applied = new List<string>();
        if (!string.IsNullOrWhiteSpace(symbol) && string.IsNullOrWhiteSpace(def.Symbol))
        {
            def.Symbol = symbol.Trim().ToUpperInvariant();
            applied.Add($"symbol {def.Symbol}");
        }
        def.Symbol = (def.Symbol ?? "").Trim().ToUpperInvariant();
        if (def.Quantity <= 0 && def.NotionalAmount is null)
        {
            def.Quantity = 1;
            applied.Add("quantity 1 share");
        }
        return applied;
    }

    /// <summary>
    /// Every check a strategy must pass before it is saved: symbol shape, canon and script
    /// round-trips, phase rules (a stop the Risk Guardian accepts, stop/target on the right side,
    /// sane percents, a scale-out ladder that sells at most 100%) and a risk preview.
    /// </summary>
    public static StrategyValidation Validate(StrategyDefinition def)
    {
        var findings = new List<StrategyFinding>();
        void Add(FindingLevel level, string rule, string message) => findings.Add(new(level, rule, message));

        string canon;
        try { canon = StrategyJson.Serialize(def); }
        catch (Exception ex)
        {
            Add(FindingLevel.Error, "canon", $"Can't write the canon: {ex.Message}");
            return new StrategyValidation("(none)", "", "", false, false, findings, []);
        }

        var stamp = def.ShortId();
        var canonRoundTrips = false;
        try { canonRoundTrips = StrategyJson.Deserialize(canon).ShortId() == stamp; }
        catch (Exception ex) { Add(FindingLevel.Error, "canon", $"The canon doesn't read back: {ex.Message}"); }
        if (!canonRoundTrips && findings.Count == 0)
            Add(FindingLevel.Error, "canon", "The canon reads back as a different strategy.");

        var script = def.ToScript();
        var scriptRoundTrips = false;
        try { scriptRoundTrips = ScriptParser.ParseScript(script)?.ShortId() == stamp; }
        catch { /* reported below */ }
        if (!scriptRoundTrips)
            Add(FindingLevel.Warning, "script", "The IdiotScript view doesn't read back exactly; edit this strategy through the CLI (canon), not the web script editor.");

        // ── Setup ──
        if (string.IsNullOrWhiteSpace(def.Symbol))
            Add(FindingLevel.Error, "symbol", "No symbol. Start the script with Stock.Ticker(\"SYM\") or pass --ticker.");
        else if (!SymbolShape.IsMatch(def.Symbol))
            Add(FindingLevel.Error, "symbol", $"'{def.Symbol}' isn't a ticker (letters, digits and '.', up to 10).");

        // ── Order ──
        if (def.Quantity < 0) Add(FindingLevel.Error, "size", "Quantity can't be negative.");
        if (def.NotionalAmount is { } n && n <= 0) Add(FindingLevel.Error, "size", "A notional amount must be above $0.");
        if (def.Quantity > 0 && def.NotionalAmount is not null)
            Add(FindingLevel.Warning, "size", "Both a share quantity and a notional amount are set; the share quantity wins.");
        if (def.Quantity <= 0 && def.NotionalAmount is null)
            Add(FindingLevel.Note, "size", "No size given: the Monitor buys 1 share.");
        if (def.Direction == TradeDirection.Short)
            Add(FindingLevel.Warning, "direction", "Short entries need a short-enabled account; Alpaca refuses them on an account that can't short.");

        // ── Entry ──
        if (def.EntryConditions.Count == 0 && def.ConditionalBlocks.Count == 0)
            Add(FindingLevel.Warning, "entry", "No entry conditions: once active it fires on the first tick it is evaluated.");

        // ── Risk (IP-LAW-2: the Risk Guardian rejects a setup without a stop) ──
        var branchStops = def.ConditionalBlocks.SelectMany(b => b.Branches).Any(b => b.Overrides.StopLossPrice is not null || b.Overrides.StopLossPercent is not null);
        if (def.StopLossPrice is null && def.StopLossPercent is null && !branchStops)
            Add(FindingLevel.Error, "stop", "No stop loss: the Risk Guardian refuses every fire without one. Add StopLoss(price) or StopLossPercent(pct).");
        if (def.StopLossPercent is { } slp && (slp <= 0 || slp >= 100))
            Add(FindingLevel.Error, "stop", $"A stop of {Inv(slp)}% is outside 0–100%.");
        if (def.StopLossPrice is { } sp && sp <= 0)
            Add(FindingLevel.Error, "stop", "A stop price must be above $0.");
        if (def.TrailingStopPercent is { } ts && (ts <= 0 || ts >= 100))
            Add(FindingLevel.Error, "trailing", $"A trailing stop of {Inv(ts)}% is outside 0–100%.");
        if (def.PeakGivebackPercent is { } pg && (pg <= 0 || pg > 100))
            Add(FindingLevel.Error, "giveback", $"A peak giveback of {Inv(pg)}% is outside 0–100%.");

        // ── Exit ──
        if (def.TakeProfitPercent is { } tpp && tpp <= 0)
            Add(FindingLevel.Error, "target", "A take-profit percent must be above 0.");
        var firstTarget = def.TakeProfitPrice ?? def.TakeProfitTargets.Select(t => (double?)t.Price).FirstOrDefault();
        if (def.StopLossPrice is { } stop && firstTarget is { } target)
        {
            var wrongSide = def.Direction == TradeDirection.Long ? stop >= target : stop <= target;
            if (wrongSide)
                Add(FindingLevel.Error, "stop/target", def.Direction == TradeDirection.Long
                    ? $"A long's stop ({Fmt(stop)}) must sit below its target ({Fmt(target)})."
                    : $"A short's stop ({Fmt(stop)}) must sit above its target ({Fmt(target)}).");
        }
        var ladder = def.TakeProfitTargets.Sum(t => t.PercentToSell);
        if (def.TakeProfitTargets.Count > 1 && ladder > 100)
            Add(FindingLevel.Error, "targets", $"The scale-out ladder sells {ladder}% of the position.");
        if (def.TakeProfitPrice is null && def.TakeProfitPercent is null && def.TakeProfitTargets.Count == 0
            && def.TrailingStopPercent is null && def.PeakGivebackPercent is null && def.ExitTime is null
            && def.RollingHighDays is null && !def.ExitAtPriorHigh)
            Add(FindingLevel.Warning, "exit", "No profit exit (target, trailing stop, giveback or sell-by time): only the stop closes the position.");

        return new StrategyValidation(stamp, canon, script, canonRoundTrips, scriptRoundTrips, findings, RiskPreview(def));
    }

    /// <summary>What one fire risks and aims for, in the strategy's own terms.</summary>
    public static List<string> RiskPreview(StrategyDefinition def)
    {
        var lines = new List<string>();
        var size = def.Quantity > 0 ? $"{def.Quantity} share{(def.Quantity == 1 ? "" : "s")}"
            : def.NotionalAmount is { } n ? $"{Rendering.Fmt.Money(n)} notional" : "1 share (default)";
        lines.Add($"{def.Direction} {size} of {def.Symbol}, {def.Session} session.");

        var entry = def.EntryConditions.OfType<PriceCondition>().Select(p => (double?)p.Price).FirstOrDefault();
        if (def.StopLossPrice is { } stop)
        {
            if (entry is { } e)
            {
                var perShare = Math.Abs(e - stop);
                var shares = def.Quantity > 0 ? def.Quantity : def.NotionalAmount is { } amt && e > 0 ? Math.Max(1, (int)Math.Floor((double)amt / e)) : 1;
                lines.Add($"Entry at {Fmt(e)}, stop {Fmt(stop)}: risks {Fmt(perShare)}/share, {Fmt(perShare * shares)} on {shares} share{(shares == 1 ? "" : "s")}.");
                var target = def.TakeProfitPrice ?? def.TakeProfitTargets.Select(t => (double?)t.Price).FirstOrDefault();
                if (target is { } t && perShare > 0)
                    lines.Add($"Target {Fmt(t)}: reward/risk {Math.Abs(t - e) / perShare:0.0}:1.");
            }
            else lines.Add($"Stop at {Fmt(stop)} (a fixed price; the risk per share depends on where it fills).");
        }
        else if (def.StopLossPercent is { } pct) lines.Add($"Stop {Inv(pct)}% from the fill.");
        if (def.TrailingStopPercent is { } tr) lines.Add($"Trailing stop {Inv(tr)}% under the best price since entry.");
        if (def.TakeProfitPercent is { } tpp) lines.Add($"Take profit {Inv(tpp)}% from the fill.");
        if (def.ExitTime is { } sell) lines.Add($"Sells anything still held at {sell:hh\\:mm} ET.");
        return lines;
    }

    /// <summary>
    /// Saves a validated definition as a new, INACTIVE strategy in <paramref name="mode"/> (never
    /// Live: going live is the separate, confirmed <c>strategy mode</c> step).
    /// </summary>
    public static async Task<Strategy> SaveAsync(OperationContext ctx, StrategyDefinition def, string? title, BrokerMode mode,
        string authorSuffix, string? description = null, string? originTranscript = null, string? author = null)
    {
        if (mode == BrokerMode.Live)
            throw OperationException.Usage("New strategies start in sandbox or paper. Go live with `idiotproof strategy mode <id> live` (it asks for confirmation).");
        var owner = ctx.RequireOwner();
        var repo = ctx.Get<StrategyRepository>();
        var created = await repo.CreateAsync(owner, TitleFor(def, title), def.Symbol, def.ToScript(),
            description: description, scriptJson: StrategyJson.Serialize(def),
            author: author ?? $"{ctx.Caller.ToString().ToLowerInvariant()}-{authorSuffix}",
            originTranscript: originTranscript, ct: ctx.CancellationToken);
        await repo.SetBrokerModeAsync([created.Id], mode.ToString(), owner, ctx.CancellationToken);
        created.BrokerMode = mode.ToString();
        return created;
    }

    /// <summary>The given title, else the strategy's own name, else "SYM strategy".</summary>
    public static string TitleFor(StrategyDefinition def, string? title)
    {
        var t = !string.IsNullOrWhiteSpace(title) ? title.Trim() : !string.IsNullOrWhiteSpace(def.Name) ? def.Name!.Trim() : $"{def.Symbol} strategy";
        return t.Length > 200 ? t[..200] : t;
    }

    /// <summary>A usage error unless the mode is one a new strategy may start in.</summary>
    public static void RequireStartMode(BrokerMode mode)
    {
        if (mode == BrokerMode.Live)
            throw OperationException.Usage("New strategies start in sandbox or paper. Go live with `idiotproof strategy mode <id> live` (it asks for confirmation).");
    }

    internal static string Fmt(double price) => Rendering.Fmt.Money((decimal)price);

    internal static string Inv(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
