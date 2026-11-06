using IdiotProof.Core.Data;
using IdiotProof.Scripting;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Services;

/// <summary>
/// Outcome of a guarded strategy mutation (activate/deactivate/delete/update).
/// Callers surface PositionOpen, NotOwner and the gapper refusals to the user;
/// NotFound is a silent no-op (row already gone).
/// <list type="bullet">
/// <item><c>PositionOpen</c> — the row holds shares: it can't be deactivated, deleted or moved to another symbol.</item>
/// <item><c>SymbolAlreadyActive</c> — a gapper can't go active while another strategy on its symbol is active.</item>
/// <item><c>GapperCapReached</c> — <see cref="StrategyRepository.MaxActiveGappers"/> gappers are already active.</item>
/// <item><c>LiveNotConfirmed</c> — the caller said it has no Live confirmation and the row routes to Live.</item>
/// </list>
/// New members are appended (existing numeric values unchanged).
/// </summary>
public enum StrategyMutation { Ok, NotFound, NotOwner, PositionOpen, SymbolAlreadyActive, GapperCapReached, LiveNotConfirmed }

/// <summary>
/// CRUD over the Strategies table on the IdiotProof SQL Server database.
/// All write operations stamp UpdatedUtc; Create assigns a UUIDv7 Id (time-ordered)
/// and CreatedUtc. The Monitor reads via <see cref="GetActiveAsync"/> to find every
/// strategy it should evaluate this tick.
/// </summary>
public sealed class StrategyRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>At most this many gappers are active at once per user (IP-LAW-11).</summary>
    public const int MaxActiveGappers = 3;

    /// <summary>
    /// Author tag of rows the auto-gapper scanner arms. Those obey one-per-symbol but not the
    /// manual 3-gapper cap: the scanner has its own per-run limit (AutoGapperMaxCount).
    /// </summary>
    public const string AutoGapperAuthor = "IdiotProof Auto-Gapper";

    /// <summary>
    /// Gapper rows are recognized by the exit-brain marker only the gapper factory emits: a
    /// <c>PeakGiveback(</c> in the script text, or (for canonical-JSON-only rows, whose text is
    /// empty) a non-null <c>peakGivebackPercent</c> in the canon — the same test the Monitor
    /// applies at fire time.
    /// </summary>
    public static bool IsGapper(Strategy s) => IsGapperScript(s.ScriptText, s.ScriptJson);

    private static readonly System.Text.RegularExpressions.Regex NullGiveback =
        new("\"peakGivebackPercent\"\\s*:\\s*null", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsGapperScript(string? scriptText, string? scriptJson)
    {
        if (scriptText?.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase) == true) return true;
        if (scriptJson is null || !scriptJson.Contains("\"peakGivebackPercent\"", StringComparison.OrdinalIgnoreCase)) return false;
        // A sequence's root is written with a null giveback even when a later step is the gapper.
        if (scriptJson.Contains("\"nextSteps\"", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return StrategyLoader.Load(scriptJson, null).Definition?.AllSteps().Any(s => s.PeakGivebackPercent.HasValue) == true;
            }
            catch
            {
                return false;
            }
        }
        return !NullGiveback.IsMatch(scriptJson);
    }

    /// <summary>
    /// The gapper activation rules (IP-LAW-11), checked against SQL for a row that is about to be
    /// (or just was) made active: no other active strategy on its symbol, and fewer than
    /// <see cref="MaxActiveGappers"/> other active gappers (auto-armed rows are exempt from the
    /// cap). Non-gapper rows always pass. Reads fresh rows, never the tracked state.
    /// </summary>
    private static async Task<StrategyMutation> CheckGapperRulesAsync(AppDbContext db, Strategy row, CancellationToken ct)
    {
        if (!IsGapper(row)) return StrategyMutation.Ok;
        var symbol = row.Symbol.ToUpperInvariant();
        var others = await db.Strategies.AsNoTracking()
            .Where(s => s.OwnerUserId == row.OwnerUserId && s.IsActive && s.Id != row.Id)
            .Select(s => new { s.Symbol, s.ScriptText, s.ScriptJson })
            .ToListAsync(ct);
        if (others.Any(o => string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            return StrategyMutation.SymbolAlreadyActive;
        if (row.Author != AutoGapperAuthor && others.Count(o => IsGapperScript(o.ScriptText, o.ScriptJson)) >= MaxActiveGappers)
            return StrategyMutation.GapperCapReached;
        return StrategyMutation.Ok;
    }

    /// <summary>
    /// Saves a row that was just switched active, then re-checks the gapper rules against SQL
    /// (closes the check-then-act race between two concurrent activations). When the recheck
    /// fails, this row's activation is reverted and the refusal returned — two racers both
    /// back off rather than both trading.
    /// </summary>
    private static async Task<StrategyMutation> SaveActivationAsync(AppDbContext db, Strategy row, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var recheck = await CheckGapperRulesAsync(db, row, ct);
        if (recheck == StrategyMutation.Ok) return StrategyMutation.Ok;
        row.IsActive = false;
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return recheck;
    }

    /// <summary>A person-facing sentence for a refused mutation (null for Ok/NotFound).</summary>
    public static string? Describe(StrategyMutation m, string symbol) => m switch
    {
        StrategyMutation.NotOwner => "That strategy belongs to another user.",
        StrategyMutation.PositionOpen => $"{symbol} is holding a position — flatten it first (the Monitor must keep managing that exit).",
        StrategyMutation.SymbolAlreadyActive => $"{symbol} already has an active strategy — one active gapper per symbol. Pause it first.",
        StrategyMutation.GapperCapReached => $"{MaxActiveGappers} gappers are already active (the cap) — pause one first.",
        StrategyMutation.LiveNotConfirmed => $"{symbol} routes to Live — activating it needs your confirmation.",
        _ => null,
    };

    public async Task<List<Strategy>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies
            .Where(s => s.OwnerUserId == userId)
            .OrderByDescending(s => s.UpdatedUtc)
            .ToListAsync(ct);
    }

    public async Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    /// <summary>
    /// Returns every active strategy across all users — the input set for the Monitor.
    /// </summary>
    public async Task<List<Strategy>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Strategies
            .Where(s => s.IsActive)
            .ToListAsync(ct);
    }

    public async Task<Strategy> CreateAsync(Guid ownerUserId, string title, string symbol,
        string scriptText, string? description = null, string? workspaceId = null,
        string? scriptJson = null, string? author = null, string? originTranscript = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var strategy = new Strategy
        {
            Id          = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            Title       = title,
            Description = description,
            Author      = author,
            OriginTranscript = originTranscript,
            Symbol      = symbol.ToUpperInvariant(),
            ScriptText  = scriptText,
            // Canonical JSON (IP-LAW-8). Callers that hold the real semantic
            // model (the Gapper factory) pass it in — zero parsing. Text-only
            // callers get it derived via the tolerant parser, which is no
            // worse than what the Monitor used to run directly.
            ScriptJson  = scriptJson ?? DeriveCanonicalJson(scriptText),
            WorkspaceId = workspaceId,
            IsActive    = false,
            CreatedUtc  = now,
            UpdatedUtc  = now,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Strategies.Add(strategy);
        await db.SaveChangesAsync(ct);
        return strategy;
    }

    /// <summary>Text → model → canonical JSON. Null when the text doesn't parse at all.</summary>
    internal static string? DeriveCanonicalJson(string scriptText)
    {
        try
        {
            var def = ScriptParser.ParseScript(scriptText);
            return def is null ? null : StrategyJson.Serialize(def);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves the EDITOR-OWNED fields only (title/symbol/description/script/
    /// IsActive). The old full-row <c>db.Update(strategy)</c> wrote every
    /// column from the caller's detached snapshot — so a Save from an editor
    /// opened minutes ago stomped the Monitor's live position bookkeeping
    /// (PositionQty/LastEntryPrice/FireCount) back to stale values: a filled
    /// position silently read as flat again, orphaning the shares AND
    /// re-arming the strategy to fire a duplicate order.
    /// Keeps the canon in lockstep with the text: unless the caller supplies
    /// canonical JSON explicitly, it is re-derived from the (edited) text.
    /// Applies the same guards as SetActiveAsync against the FRESH row, and refuses a symbol
    /// change while the row holds a position (PositionOpen): the shares are in the old symbol,
    /// so the Monitor would manage the wrong ticker's exit and strand them.
    /// When the save turns the row active (or changes the symbol/script of an active row), the
    /// gapper rules apply as in <see cref="SetActiveAsync"/>. Pass
    /// <paramref name="liveActivationConfirmed"/> = false from a surface that has not confirmed
    /// a Live activation: turning on a row that (freshly) routes to Live is then refused.
    /// </summary>
    public async Task<StrategyMutation> UpdateAsync(Strategy strategy, string? scriptJson = null, CancellationToken ct = default,
        bool liveActivationConfirmed = true)
    {
        var canon = scriptJson ?? DeriveCanonicalJson(strategy.ScriptText);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Strategies.FirstOrDefaultAsync(s => s.Id == strategy.Id, ct);
        if (row is null) return StrategyMutation.NotFound;
        if (row.OwnerUserId != strategy.OwnerUserId) return StrategyMutation.NotOwner;
        if (row.PositionQty > 0 && row.IsActive && !strategy.IsActive) return StrategyMutation.PositionOpen;

        var newSymbol = strategy.Symbol.ToUpperInvariant();
        var symbolChanged = !string.Equals(row.Symbol, newSymbol, StringComparison.OrdinalIgnoreCase);
        if (row.PositionQty > 0 && symbolChanged) return StrategyMutation.PositionOpen;

        var activating = strategy.IsActive && !row.IsActive;
        if (activating && !liveActivationConfirmed
            && string.Equals(row.BrokerMode, "Live", StringComparison.OrdinalIgnoreCase))
            return StrategyMutation.LiveNotConfirmed;
        var wasGapper = IsGapper(row);

        row.Title       = strategy.Title;
        row.Symbol      = newSymbol;
        row.Description = strategy.Description;
        row.ScriptText  = strategy.ScriptText;
        row.ScriptJson  = canon;
        row.IsActive    = strategy.IsActive;
        // BrokerMode is NOT editor-owned: it changes only through SetBrokerModeAsync (behind the
        // Live gate). Writing the caller's snapshot here let a stale editor save silently revert a
        // Paper switch back to Live, with no elevation or code.
        row.UpdatedUtc  = DateTime.UtcNow;

        // Gapper rules (IP-LAW-11) whenever this save puts a gapper into the active set or moves
        // an active one: turning it on, re-pointing it at another symbol, or editing a script
        // into a gapper. A plain re-save of an already-active gapper is left alone.
        var gapperRulesApply = row.IsActive && (activating || symbolChanged || !wasGapper);
        if (gapperRulesApply)
        {
            var pre = await CheckGapperRulesAsync(db, row, ct);
            if (pre != StrategyMutation.Ok) return pre; // nothing saved: the tracked row is discarded
            var post = await SaveActivationAsync(db, row, ct);
            if (post != StrategyMutation.Ok) return post;
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        strategy.Symbol = newSymbol;
        strategy.ScriptJson = canon;
        strategy.UpdatedUtc = row.UpdatedUtc;
        return StrategyMutation.Ok;
    }

    /// <summary>
    /// One-shot legacy backfill (IP-LAW-8): derives canonical JSON for every row
    /// written before the ScriptJson column existed. Runs at Blazor startup;
    /// cheap no-op once all rows carry a canon. Returns how many were filled.
    /// </summary>
    public async Task<int> BackfillCanonicalJsonAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var legacy = await db.Strategies.Where(s => s.ScriptJson == null).ToListAsync(ct);
        var filled = 0;
        foreach (var s in legacy)
        {
            var json = DeriveCanonicalJson(s.ScriptText);
            if (json is null) continue; // unparseable text stays legacy; Monitor already skips it
            s.ScriptJson = json;
            filled++;
        }
        if (filled > 0) await db.SaveChangesAsync(ct);
        return filled;
    }

    /// <summary>Outcome of a canon re-derivation sweep (see <see cref="ResyncCanonFromTextAsync"/>).</summary>
    public sealed record CanonResyncResult(int Scanned, int Changed, int SkippedRegression, List<string> Notes);

    /// <summary>
    /// Re-derives each strategy's canonical JSON from its ScriptText and rewrites
    /// ScriptJson where the text now yields a RICHER canon than what is stored —
    /// the repair for verbs a since-fixed <see cref="ScriptParser"/> used to drop
    /// silently (e.g. IsHigherLow), which left the money-path canon (IP-LAW-8)
    /// missing conditions the script clearly declared.
    ///
    /// SAFE BY CONSTRUCTION: a row is rewritten only when the re-derived canon
    /// parses AND has at least as many entry conditions and conditional blocks as
    /// the stored canon. Any row where re-derivation would LOSE something (e.g. a
    /// branching strategy the tolerant parser can't reconstruct) is skipped and
    /// reported — never regressed. Touches ScriptJson + UpdatedUtc only, so live
    /// position bookkeeping is preserved. Pass <paramref name="apply"/>=false for
    /// a dry run. <paramref name="ownerUserId"/> limits the sweep to one user's rows
    /// (the CLI/MCP <c>strategy resync-canon</c>); null sweeps every row (the Monitor CLI).
    /// </summary>
    public async Task<CanonResyncResult> ResyncCanonFromTextAsync(bool apply, CancellationToken ct = default, Guid? ownerUserId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var all = await db.Strategies.Where(s => ownerUserId == null || s.OwnerUserId == ownerUserId).ToListAsync(ct);
        var notes = new List<string>();
        int changed = 0, skipped = 0;

        foreach (var s in all)
        {
            if (string.IsNullOrWhiteSpace(s.ScriptText)) continue;
            var newJson = DeriveCanonicalJson(s.ScriptText);
            if (newJson is null) { notes.Add($"skip  {s.Symbol} \"{s.Title}\" — ScriptText does not parse"); continue; }
            if (newJson == s.ScriptJson) continue; // already in sync

            StrategyDefinition? oldDef = null, newDef = null;
            try { oldDef = string.IsNullOrWhiteSpace(s.ScriptJson) ? null : StrategyJson.Deserialize(s.ScriptJson); } catch { /* treat unreadable stored canon as empty */ }
            try { newDef = StrategyJson.Deserialize(newJson); } catch { }
            if (newDef is null) { notes.Add($"skip  {s.Symbol} \"{s.Title}\" — re-derived canon invalid"); continue; }

            var oldEntry = oldDef?.EntryConditions.Count ?? 0;
            var oldBranch = oldDef?.ConditionalBlocks.Count ?? 0;
            if (newDef.EntryConditions.Count < oldEntry || newDef.ConditionalBlocks.Count < oldBranch)
            {
                skipped++;
                notes.Add($"SKIP  {s.Symbol} \"{s.Title}\" — would REGRESS (entry {oldEntry}→{newDef.EntryConditions.Count}, branches {oldBranch}→{newDef.ConditionalBlocks.Count})");
                continue;
            }

            notes.Add($"{(apply ? "FIX  " : "WOULD")} {s.Symbol} \"{s.Title}\" — entry {oldEntry}→{newDef.EntryConditions.Count}, branches {oldBranch}→{newDef.ConditionalBlocks.Count}");
            if (apply)
            {
                s.ScriptJson = newJson;
                s.UpdatedUtc = DateTime.UtcNow;
            }
            changed++;
        }

        if (apply && changed > 0) await db.SaveChangesAsync(ct);
        return new CanonResyncResult(all.Count, changed, skipped, notes);
    }

    /// <summary>
    /// Toggles IsActive with the two guards every mutating caller must obey:
    /// ownership (the row must belong to <paramref name="ownerUserId"/> — no
    /// caller may flip another user's strategy) and open-position safety
    /// (deactivating a row with PositionQty &gt; 0 would orphan the position:
    /// the Monitor only evaluates active rows, so the stop/giveback/sell-by
    /// brain would never run again while the broker still holds shares).
    /// Activating a gapper also enforces one active strategy per symbol and the
    /// <see cref="MaxActiveGappers"/> cap against SQL, with a post-write recheck
    /// (IP-LAW-11) — so every surface (web, CLI, MCP, bulk, scanners) is held to it.
    /// Re-activating an already-active row is a no-op Ok.
    /// A surface that has not confirmed a Live activation passes
    /// <paramref name="liveActivationConfirmed"/> = false: activating a row whose FRESH
    /// BrokerMode is Live is then refused with LiveNotConfirmed (so a stale page that still
    /// shows Paper can't turn on a strategy someone has since routed to Live).
    /// </summary>
    public async Task<StrategyMutation> SetActiveAsync(Guid id, bool isActive, Guid ownerUserId, CancellationToken ct = default,
        bool liveActivationConfirmed = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return StrategyMutation.NotFound;
        if (strategy.OwnerUserId != ownerUserId) return StrategyMutation.NotOwner;
        if (!isActive && strategy.PositionQty > 0) return StrategyMutation.PositionOpen;
        if (isActive && strategy.IsActive) return StrategyMutation.Ok;
        if (isActive && !liveActivationConfirmed && IsLive(strategy)) return StrategyMutation.LiveNotConfirmed;
        return await ApplyActiveAsync(db, strategy, isActive, ct);
    }

    private static bool IsLive(Strategy s) => string.Equals(s.BrokerMode, "Live", StringComparison.OrdinalIgnoreCase);

    /// <summary>Flips one tracked row's IsActive and saves it, applying the gapper rules on activation.</summary>
    private static async Task<StrategyMutation> ApplyActiveAsync(AppDbContext db, Strategy strategy, bool isActive, CancellationToken ct)
    {
        if (isActive)
        {
            var pre = await CheckGapperRulesAsync(db, strategy, ct);
            if (pre != StrategyMutation.Ok) return pre;
        }
        strategy.IsActive = isActive;
        strategy.UpdatedUtc = DateTime.UtcNow;
        if (isActive) return await SaveActivationAsync(db, strategy, ct);
        await db.SaveChangesAsync(ct);
        return StrategyMutation.Ok;
    }

    /// <summary>
    /// Deletes a strategy row — same ownership + open-position guards as
    /// <see cref="SetActiveAsync"/>. Deleting a holding row would permanently
    /// discard the entry price/quantity/exit rules for shares the broker
    /// still holds; flatten first.
    /// </summary>
    public async Task<StrategyMutation> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return StrategyMutation.NotFound;
        if (strategy.OwnerUserId != ownerUserId) return StrategyMutation.NotOwner;
        if (strategy.PositionQty > 0) return StrategyMutation.PositionOpen;
        db.Strategies.Remove(strategy);

        // Take the strategy's ConditionProgress row with it — there is no FK,
        // so the per-tick badge rows for deleted strategies used to orphan
        // and accumulate forever in a table the Monitor hammers.
        var progress = await db.ConditionProgress.FirstOrDefaultAsync(p => p.StrategyId == id, ct);
        if (progress is not null) db.ConditionProgress.Remove(progress);

        await db.SaveChangesAsync(ct);
        return StrategyMutation.Ok;
    }

    /// <summary>
    /// Counts this user's ACTIVE strategies for a symbol straight from SQL —
    /// the authoritative duplicate/cap check (an in-memory page list can be a
    /// poll-interval stale).
    /// </summary>
    public async Task<int> CountActiveForSymbolAsync(Guid ownerUserId, string symbol, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var upper = symbol.ToUpperInvariant();
        return await db.Strategies.CountAsync(
            s => s.OwnerUserId == ownerUserId && s.IsActive && s.Symbol == upper, ct);
    }

    /// <summary>
    /// Counts this user's strategies that currently HOLD a position in a symbol
    /// (PositionQty &gt; 0). When &gt;1, the broker's per-symbol position is shared
    /// across strategies and can't be attributed to one — the exit path must
    /// then trust per-strategy bookkeeping rather than the broker aggregate
    /// (multi-strategy-per-ticker support).
    /// </summary>
    public async Task<int> CountHoldingForSymbolAsync(Guid ownerUserId, string symbol, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var upper = symbol.ToUpperInvariant();
        return await db.Strategies.CountAsync(
            s => s.OwnerUserId == ownerUserId && s.PositionQty > 0 && s.Symbol == upper, ct);
    }

    /// <summary>
    /// Bumps FireCount + LastFiredUtc when the Monitor reports a signal fired
    /// for this strategy. Stays minimal — full TradeSignal log is a separate table.
    /// </summary>
    public async Task RecordFiredAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.LastFiredUtc = DateTime.UtcNow;
        strategy.FireCount++;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records an entry fill: the Monitor now manages an open position for
    /// this strategy and will evaluate exit rules instead of entry conditions.
    /// </summary>
    public async Task RecordEntryFillAsync(
        Guid id, int quantity, decimal fillPrice, DateTime filledUtc, CancellationToken ct = default,
        string? resolvedScriptJson = null, bool countsAsCycle = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.PositionQty    = quantity;
        strategy.LastEntryPrice = fillPrice;
        strategy.EntryFilledUtc = filledUtc;
        // Repeat(n) counts sequences, not steps: only a step-0 fill is a new cycle.
        if (countsAsCycle)
        {
            var cycleDay = Strategy.EtDay(filledUtc);
            strategy.CyclesToday = strategy.CycleDateEt == cycleDay ? strategy.CyclesToday + 1 : 1;
            strategy.CycleDateEt = cycleDay;
        }
        strategy.AwaitingTransition = false;
        strategy.LastExitedUtc  = null;
        strategy.LastExitPrice  = null;
        strategy.LastExitReason = null;
        strategy.ResolvedEntryScriptJson = resolvedScriptJson;
        strategy.InitialPositionQty      = quantity;
        strategy.UpdatedUtc     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fully clears entry bookkeeping for a position that NEVER FILLED (the
    /// optimistic RecordEntryFillAsync stamped a fill the broker never made).
    /// Unlike <see cref="RecordExitFillAsync"/>, this also clears
    /// <c>EntryFilledUtc</c> and <c>LastEntryPrice</c> so the one-shot-per-day
    /// guard does NOT treat the strategy as "already traded today" — a genuine
    /// non-fill must be free to re-arm and re-enter within its window, not be
    /// locked out for the day by a phantom.
    /// </summary>
    public async Task ClearUnfilledEntryAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        // The phantom fill counted as a cycle; give it back so Repeat(n) isn't shortchanged.
        if (strategy.StepIndex == 0 && strategy.EntryFilledUtc is { } phantom && strategy.CycleDateEt == Strategy.EtDay(phantom) && strategy.CyclesToday > 0)
            strategy.CyclesToday--;
        strategy.PositionQty    = 0;
        strategy.LastEntryPrice = null;
        strategy.EntryFilledUtc = null;
        strategy.ResolvedEntryScriptJson = null;
        strategy.InitialPositionQty      = null;
        strategy.UpdatedUtc     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records an exit fill: flattens the tracked position and stamps the
    /// exit bookkeeping the UI renders ("sold 09:22 — PeakGiveback @ 11.48").
    /// With <paramref name="moreSteps"/> the sequence now waits, flat, for this step's ThenWhen.
    /// </summary>
    public async Task RecordExitFillAsync(Guid id, decimal exitPrice, string reason, DateTime exitedUtc, CancellationToken ct = default,
        bool moreSteps = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.PositionQty    = 0;
        strategy.LastExitPrice  = exitPrice;
        strategy.LastExitReason = reason;
        strategy.LastExitedUtc  = exitedUtc;
        strategy.AwaitingTransition = moreSteps;
        strategy.ResolvedEntryScriptJson = null;
        strategy.InitialPositionQty      = null;
        strategy.UpdatedUtc     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Arms step <paramref name="nextIndex"/> of a sequence: its ThenWhen held while flat.</summary>
    public async Task AdvanceStepAsync(Guid id, int nextIndex, DateTime utcNow, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.StepIndex          = nextIndex;
        strategy.AwaitingTransition = false;
        strategy.StepArmedUtc       = utcNow;
        strategy.UpdatedUtc         = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Puts a sequence back at step 0 (the Monitor after the last step with Repeat, or a person via
    /// <c>strategy step --reset</c>). Refused with PositionOpen while the row holds shares: the step
    /// that opened them must keep managing the exit. With <paramref name="ownerUserId"/> the row
    /// must belong to that user.
    /// </summary>
    public async Task<StrategyMutation> ResetSequenceAsync(Guid id, Guid? ownerUserId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return StrategyMutation.NotFound;
        if (ownerUserId is { } owner && strategy.OwnerUserId != owner) return StrategyMutation.NotOwner;
        if (strategy.PositionQty > 0) return StrategyMutation.PositionOpen;
        strategy.StepIndex          = 0;
        strategy.AwaitingTransition = false;
        strategy.StepArmedUtc       = null;
        strategy.UpdatedUtc         = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return StrategyMutation.Ok;
    }

    /// <summary>
    /// Records a partial scale-out fill: reduces PositionQty by the sold quantity
    /// without closing the position.  EntryFilledUtc and LastEntryPrice are preserved
    /// so the exit evaluator can continue managing the remaining shares.
    /// Used when a multi-target TakeProfit ladder sells one rung at a time.
    /// </summary>
    public async Task RecordPartialExitAsync(Guid id, int quantitySold, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var strategy = await db.Strategies.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (strategy is null) return;
        strategy.PositionQty = Math.Max(0, strategy.PositionQty - quantitySold);
        strategy.UpdatedUtc  = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stamps a strategy row with the entry price and fill time without touching any
    /// other fields. Used by the Monitor to bootstrap cost basis from a live broker
    /// position when a strategy was created with PositionQty > 0 but no entry price.
    /// </summary>
    public async Task SetEntryBookkeepingAsync(Guid strategyId, decimal entryPrice, DateTime filledUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Strategies.FindAsync([strategyId], ct);
        if (row is null) return;
        row.LastEntryPrice = entryPrice;
        row.EntryFilledUtc = filledUtc;
        row.UpdatedUtc     = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sets BrokerMode ("Paper" | "Live" | "Sandbox") for a batch of strategies owned
    /// by <paramref name="ownerUserId"/>. Skips strategies not owned by that user, and
    /// strategies holding a position (re-routing those would strand the shares in the old
    /// account: the exit would go to the new one). Returns the count of rows actually updated.
    /// </summary>
    public async Task<int> SetBrokerModeAsync(
        IReadOnlyCollection<Guid> ids, string mode, Guid ownerUserId, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Strategies
            .Where(s => ids.Contains(s.Id) && s.OwnerUserId == ownerUserId && s.PositionQty <= 0)
            .ToListAsync(ct);
        foreach (var r in rows)
        {
            r.BrokerMode = mode;
            r.UpdatedUtc = DateTime.UtcNow;
        }
        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Bulk-activates or deactivates a set of strategies owned by
    /// <paramref name="ownerUserId"/>. Applies the same open-position guard as
    /// <see cref="SetActiveAsync"/>: strategies holding a position cannot be
    /// deactivated. Activation applies the gapper rules row by row (each row is
    /// saved and re-checked before the next is considered, so a batch can't push
    /// past the cap or arm two gappers on one symbol). Rows already in the target
    /// state are left alone and not counted. With
    /// <paramref name="liveActivationConfirmed"/> = false, Live rows are not activated
    /// (counted as SkippedLiveUnconfirmed). Returns the per-outcome counts.
    /// </summary>
    public async Task<(int Updated, int SkippedPositionOpen, int SkippedGapperRule, int SkippedLiveUnconfirmed)> SetActiveBulkAsync(
        IReadOnlyCollection<Guid> ids, bool isActive, Guid ownerUserId, CancellationToken ct = default,
        bool liveActivationConfirmed = true)
    {
        if (ids.Count == 0) return (0, 0, 0, 0);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Strategies
            .Where(s => ids.Contains(s.Id) && s.OwnerUserId == ownerUserId)
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);
        int updated = 0, skippedPosition = 0, skippedGapper = 0, skippedLive = 0;
        foreach (var r in rows)
        {
            if (r.IsActive == isActive) continue;
            if (!isActive && r.PositionQty > 0) { skippedPosition++; continue; }
            if (isActive && !liveActivationConfirmed && IsLive(r)) { skippedLive++; continue; }
            var m = await ApplyActiveAsync(db, r, isActive, ct);
            if (m == StrategyMutation.Ok) updated++;
            else skippedGapper++;
        }
        return (updated, skippedPosition, skippedGapper, skippedLive);
    }

    /// <summary>
    /// Marks a newly created strategy as already holding an open position so the Monitor
    /// immediately manages its exit (orphan bootstrap path). Sets PositionQty = qty,
    /// BrokerMode = "Live", and IsActive = true. Used by the builder when the user
    /// disambiguates "close existing position" for a ticker they already hold.
    /// </summary>
    public async Task MarkAsExistingPositionAsync(Guid strategyId, int qty, Guid ownerUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Strategies.FindAsync([strategyId], ct);
        if (row is null || row.OwnerUserId != ownerUserId) return;
        row.PositionQty = qty;
        // The adopted quantity IS the "initial" size for scale-out ladder math
        // going forward — no branch resolution ran for an orphan adoption, so
        // ResolvedEntryScriptJson stays null (exit falls back to the raw
        // definition, which is all there is to go on here anyway).
        row.InitialPositionQty = qty;
        row.BrokerMode  = "Live";
        row.IsActive    = true;
        row.UpdatedUtc  = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
