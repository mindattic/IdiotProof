using System.Globalization;
using System.Text.Json;
using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using IdiotProof.Core.Monitoring;
using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Catalyst;

/// <summary>Shared pieces of the <c>catalyst</c> operations (IP-T443).</summary>
internal static class CatalystReads
{
    /// <summary><c>today</c>, <c>yesterday</c>, <c>6h</c>, <c>3d</c>, or a date / date-time (ET); null → the default window.</summary>
    public static DateTime? ParseSince(string? text, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant();
        var todayEt = EasternClock.ToEastern(nowUtc).Date;
        if (t == "today") return EasternClock.ToUtcFromEastern(todayEt);
        if (t == "yesterday") return EasternClock.ToUtcFromEastern(todayEt.AddDays(-1));
        if (t.Length > 1 && int.TryParse(t[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            switch (t[^1])
            {
                case 'h': return nowUtc.AddHours(-n);
                case 'd': return nowUtc.AddDays(-n);
                case 'm': return nowUtc.AddMinutes(-n);
            }
        }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return EasternClock.ToUtcFromEastern(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        throw OperationException.Usage($"--since '{text}' is not today, yesterday, Nh, Nd, or a date (ET).");
    }

    public static async Task<CatalystEvent> ResolveAsync(OperationContext ctx, string reference)
    {
        var r = (reference ?? "").Trim().Replace("-", "");
        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        if (Guid.TryParse(reference, out var id))
            return await db.CatalystEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ctx.CancellationToken)
                ?? throw OperationException.Usage($"No catalyst event has id {id}.");
        if (r.Length < 6 || !r.All(Uri.IsHexDigit))
            throw OperationException.Usage("Name a catalyst event by its id or an id prefix of at least 6 hex characters (`idiotproof catalyst list` shows them).");
        var candidates = await db.CatalystEvents.AsNoTracking().OrderByDescending(e => e.ReceivedUtc).Take(5000).ToListAsync(ctx.CancellationToken);
        var matches = candidates.Where(e => e.Id.ToString("N").StartsWith(r, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw OperationException.Usage($"No recent catalyst event matches '{reference}'."),
            _ => throw OperationException.Usage($"'{reference}' matches {matches.Count} events — give more of the id."),
        };
    }

    public static CatalystScoreBreakdown Explain(CatalystEvent e, DateTime nowUtc)
    {
        var preClass = Enum.TryParse<CatalystLegacyClass>(e.PreClass, out var pc) ? pc : CatalystLegacyClass.Other;
        return CatalystScorer.Explain(preClass, e.Direction, e.LlmMagnitude, e.SourceTier, e.NoveltyCount, Math.Max(0, (nowUtc - e.PublishedUtc).TotalMinutes));
    }

    public static string Short(Guid id) => id.ToString("N")[..8];

    /// <summary>The stored catalyst type (a row from before IP-T487 has none: its version 1 class read as the nearest type), with the LOI flag.</summary>
    public static string Type(CatalystEvent e) => (CatalystPreClassNames.Parse(e.CatalystType ?? e.PreClass) ?? CatalystPreClass.Other) + (e.NonBindingLoi ? " (LOI)" : "");

    public static string TypeLabel(CatalystEvent e) =>
        CatalystPreClassNames.Label(CatalystPreClassNames.Parse(e.CatalystType ?? e.PreClass) ?? CatalystPreClass.Other) + (e.NonBindingLoi ? ", non-binding (LOI / MOU / term sheet)" : "") + (e.CatalystType is null ? " (not typed yet: from the version 1 class)" : "");

    public static string Dir(int d) => d switch { > 0 => "+1", < 0 => "-1", _ => "0" };

    public static string Lag(CatalystEvent e) =>
        e.Backfilled ? "—" : Math.Max(0, (e.ReceivedUtc - e.PublishedUtc).TotalSeconds).ToString("0", CultureInfo.InvariantCulture) + "s";

    public static List<string> Symbols(CatalystEvent e)
    {
        try { return JsonSerializer.Deserialize<List<string>>(e.SymbolsJson) ?? [e.PrimarySymbol]; }
        catch (JsonException) { return [e.PrimarySymbol]; }
    }

    public static object Row(CatalystEvent e) => new
    {
        e.Id, e.Source, e.ExternalId, e.PublishedUtc, e.ReceivedUtc, LagSeconds = e.Backfilled ? (double?)null : (e.ReceivedUtc - e.PublishedUtc).TotalSeconds,
        e.PrimarySymbol, Symbols = Symbols(e), e.Headline, e.Url, e.FormType, e.ItemsJson, e.SourceTier,
        e.CatalystType, e.NonBindingLoi, e.PreClass, e.PreDirection, e.PreConfidence, e.LlmType, e.LlmSentiment, e.LlmMagnitude, e.LlmHasHappened, e.LlmMechanism, e.LlmClassifiedUtc,
        e.NoveltyCount, e.CatalystScore, e.Direction, e.ScoreComputedUtc, e.Backfilled,
    };
}

public sealed record CatalystListArgs
{
    [Option("symbol", Description = "Only events naming this symbol.", Short = 's')]
    public string? Symbol { get; init; }

    [Option("since", Description = "Window start: today, yesterday, 6h, 3d, or a date (ET). Default 24h.")]
    public string? Since { get; init; }

    [Option("limit", Description = "Most recent N events (default 50, max 500).", Short = 'n')]
    public int Limit { get; init; } = 50;
}

[Operation("catalyst list", Summary = "Catalyst events the intake recorded (news and EDGAR filings): published ET, receive lag, symbol, catalyst type, direction, score, source, headline.", Risk = OperationRisk.Read)]
[Example("idiotproof catalyst list --since today")]
[Example("idiotproof catalyst list --symbol ABCD --since 3d")]
public sealed class CatalystListOperation : IOperation<CatalystListArgs>
{
    public async Task<OperationResult> ExecuteAsync(CatalystListArgs args, OperationContext ctx)
    {
        if (args.Limit is < 1 or > 500) return OperationResult.Usage("--limit must be 1-500.");
        var since = CatalystReads.ParseSince(args.Since, ctx.UtcNow) ?? ctx.UtcNow.AddHours(-24);
        var symbol = args.Symbol?.Trim().ToUpperInvariant();

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var query = db.CatalystEvents.AsNoTracking().Where(e => e.PublishedUtc >= since);
        if (!string.IsNullOrEmpty(symbol))
            query = query.Where(e => db.CatalystEventSymbols.Any(s => s.EventId == e.Id && s.Symbol == symbol));
        var rows = await query.OrderByDescending(e => e.PublishedUtc).Take(args.Limit).ToListAsync(ctx.CancellationToken);
        if (rows.Count == 0)
            return OperationResult.Ok(Array.Empty<object>(), $"No catalyst events since {ctx.Et(since)}{(symbol is null ? "" : $" for {symbol}")}. " +
                "`idiotproof catalyst intake-status` shows whether the intake is running; `catalyst backfill` fills history.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id").Column("published ET").Column("lag", Align.Right).Column("sym").Column("type").Column("dir", Align.Right)
            .Column("score", Align.Right).Column("src").Column("headline", maxWidth: 70);
        foreach (var e in rows)
            table.Row(CatalystReads.Short(e.Id), Fmt.Et(e.PublishedUtc), CatalystReads.Lag(e), e.PrimarySymbol, CatalystReads.Type(e), CatalystReads.Dir(e.Direction),
                e.CatalystScore.ToString("0.#", CultureInfo.InvariantCulture),
                e.Source == CatalystIntake.Edgar ? $"edgar {e.FormType}" : e.Source == CatalystIntake.AlpacaNews ? "news" : e.Source, // IP-T602: a wire keeps its own name, not "news"
                e.Headline);
        return OperationResult.Ok(rows.Select(CatalystReads.Row).ToList(),
            $"Catalyst events since {ctx.Et(since)}{(symbol is null ? "" : $" for {symbol}")}:\n{table.Render()}\n{rows.Count} event(s). `idiotproof catalyst show <id>` explains one.");
    }
}

public sealed record CatalystShowArgs
{
    [Argument(0, Name = "id", Description = "The event id (or an id prefix of 6+ hex characters).")]
    public string Id { get; init; } = "";
}

[Operation("catalyst show", Summary = "Every field of one catalyst event, with its score broken down as of now.", Risk = OperationRisk.Read)]
[Example("idiotproof catalyst show 0190abcd")]
public sealed class CatalystShowOperation : IOperation<CatalystShowArgs>
{
    public async Task<OperationResult> ExecuteAsync(CatalystShowArgs args, OperationContext ctx)
    {
        var e = await CatalystReads.ResolveAsync(ctx, args.Id);
        var breakdown = CatalystReads.Explain(e, ctx.UtcNow);
        var block = new KeyValueBlock()
            .Add("Id", e.Id.ToString())
            .Add("Source", e.Source == CatalystIntake.Edgar ? $"edgar {e.FormType} {e.ExternalId}" : $"{e.Source} {e.ExternalId}")
            .Add("Published", Fmt.Et(e.PublishedUtc))
            .Add("Received", e.Backfilled ? "backfilled (no latency known)" : $"{Fmt.Et(e.ReceivedUtc)} (lag {CatalystReads.Lag(e)})")
            .Add("Symbols", string.Join(", ", CatalystReads.Symbols(e)) + $" (primary {e.PrimarySymbol})")
            .Add("Headline", e.Headline)
            .Add("Url", e.Url)
            .Add("Items", e.ItemsJson)
            .Add("Source tier", e.SourceTier.ToString(CultureInfo.InvariantCulture))
            .Add("Catalyst type", CatalystReads.TypeLabel(e)).Add("Version 1 class", $"{e.PreClass} dir {CatalystReads.Dir(e.PreDirection)} conf {e.PreConfidence.ToString("0.##", CultureInfo.InvariantCulture)} (frozen; the score uses it)")
            .Add("LLM", e.LlmClassifiedUtc is null ? "not classified" :
                $"{e.LlmType} {e.LlmSentiment} {e.LlmMagnitude}{(e.LlmHasHappened is { } h ? (h ? " (happened)" : " (pending)") : "")} at {Fmt.Et(e.LlmClassifiedUtc)}")
            .Add("Mechanism", e.LlmMechanism)
            .Add("Novelty", $"{e.NoveltyCount} earlier event(s) for {e.PrimarySymbol} in the window")
            .Add("Stored score", $"{e.CatalystScore.ToString("0.#", CultureInfo.InvariantCulture)} dir {CatalystReads.Dir(e.Direction)} at {Fmt.Et(e.ScoreComputedUtc)}")
            .Add("Score now", breakdown.Describe())
            .Add("Snippet", e.RawSnippet is { Length: > 0 } s ? (s.Length > 600 ? s[..600] + "…" : s) : null);
        return OperationResult.Ok(new { Event = CatalystReads.Row(e), Breakdown = breakdown }, block.Render());
    }
}

public sealed record CatalystScoreArgs
{
    [Argument(0, Name = "symbol", Description = "The symbol whose latest catalyst to score.")]
    public string Symbol { get; init; } = "";
}

[Operation("catalyst score", Summary = "The latest catalyst event for a symbol and its score breakdown as of now (type prior × magnitude × source + novelty, decayed by age).", Risk = OperationRisk.Read)]
[Example("idiotproof catalyst score ABCD")]
public sealed class CatalystScoreOperation : IOperation<CatalystScoreArgs>
{
    public async Task<OperationResult> ExecuteAsync(CatalystScoreArgs args, OperationContext ctx)
    {
        var symbol = args.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return OperationResult.Usage("Name a symbol.");
        var e = await ctx.Get<CatalystLookup>().LatestForSymbolAsync(symbol, ctx.UtcNow, ctx.CancellationToken);
        if (e is null) return OperationResult.Ok(null, $"No catalyst event recorded for {symbol}.");
        var b = CatalystReads.Explain(e, ctx.UtcNow);
        var text = $"{symbol}: {CatalystReads.Type(e)} {CatalystReads.Dir(b.Direction)} published {Fmt.Et(e.PublishedUtc)} ({b.AgeMinutes:0} min ago) — \"{e.Headline}\"\n" +
                   $"score now {b.Score:0.#}: {b.Describe()}\n" +
                   $"(stored {e.CatalystScore:0.#} at {Fmt.Et(e.ScoreComputedUtc)}; event {CatalystReads.Short(e.Id)})";
        return OperationResult.Ok(new { Event = CatalystReads.Row(e), Breakdown = b }, text);
    }
}

[Operation("catalyst intake-status", Summary = "Whether the catalyst intake is enabled, who owns it, whether this Hub leads, the news stream's health, the last message ET, EDGAR polling and today's counts.", Risk = OperationRisk.Read)]
[Example("idiotproof catalyst intake-status")]
public sealed class CatalystIntakeStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var config = await ctx.Get<CatalystIntakeSettings>().ReadAsync(ctx.CancellationToken);
        var hosted = ctx.Services.GetService<MonitorRuntimeState>() is not null;
        var state = ctx.Services.GetService<CatalystIntakeState>()?.Snapshot();
        var classifier = ctx.Services.GetService<CatalystClassifier>();
        var dayStart = EasternClock.ToUtcFromEastern(EasternClock.ToEastern(ctx.UtcNow).Date);

        await using var db = await ctx.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ctx.CancellationToken);
        var today = await db.CatalystEvents.AsNoTracking().Where(e => e.ReceivedUtc >= dayStart && !e.Backfilled)
            .GroupBy(e => e.Source).Select(g => new { Source = g.Key, Count = g.Count(), Classified = g.Count(e => e.LlmClassifiedUtc != null) })
            .ToListAsync(ctx.CancellationToken);
        var latest = await db.CatalystEvents.AsNoTracking().Where(e => !e.Backfilled).OrderByDescending(e => e.ReceivedUtc)
            .Select(e => (DateTime?)e.ReceivedUtc).FirstOrDefaultAsync(ctx.CancellationToken);

        var news = today.FirstOrDefault(t => t.Source == CatalystIntake.AlpacaNews);
        var edgar = today.FirstOrDefault(t => t.Source == CatalystIntake.Edgar);
        var stream = state?.Stream;
        var block = new KeyValueBlock()
            .Add("Enabled", config.Enabled ? "yes" : $"no (set {CatalystIntakeSettings.EnabledKey} = true)")
            .Add("Owner", config.OwnerUserId?.ToString() ?? $"unset ({CatalystIntakeSettings.OwnerKey})")
            .Add("Hosted", hosted ? "yes (this Hub hosts the Monitor)" : "no (this Hub does not host the Monitor; the intake runs with it)")
            .Add("Leader", state is null ? "—" : state.Leader ? "yes" : "no (standby)")
            .Add("Stream", stream is null ? (state?.Running == true ? "starting" : "not running") :
                $"{stream.State}{(stream.ConnectedSinceUtc is { } c ? $" since {Fmt.Et(c)}" : "")}, {stream.Messages} message(s), {stream.Reconnects} reconnect(s)")
            .Add("Last message", Fmt.Et(state?.LastMessageUtc ?? stream?.LastMessageUtc, "never"))
            .Add("Session counts", state is null ? null : $"{state.NewsMessages} news seen, {state.NewsInserted} stored, {state.NewsDuplicates} duplicate(s), {state.NewsNoSymbol} without a symbol")
            .Add("EDGAR polling", !config.EdgarPolling ? $"off ({CatalystIntakeSettings.EdgarPollingKey})" : state?.EdgarLastPoll is { } p
                ? $"on; last poll {Fmt.Et(state.EdgarLastPollUtc)}: {p.Entries} feed entries, {p.Candidates} new, {p.Inserted} stored, {p.Unmapped} without a ticker, {p.Failed} failed"
                : "on; no poll yet")
            .Add("LLM classify", !config.LlmClassify ? $"off ({CatalystIntakeSettings.LlmClassifyKey})" : classifier is null ? $"on; budget {config.LlmDailyBudget}/day ET" :
                $"on; {classifier.UsedToday}/{config.LlmDailyBudget} of today's budget ({CatalystIntakeSettings.LlmDailyBudgetKey}), {classifier.Pending} queued, {classifier.Classified} classified, {classifier.Failed} failed, {classifier.OverBudget} over budget, {classifier.Dropped} dropped")
            .Add("Today (ET)", $"news {news?.Count ?? 0} ({news?.Classified ?? 0} LLM-classified), EDGAR {edgar?.Count ?? 0} ({edgar?.Classified ?? 0} LLM-classified)")
            .Add("Latest stored", Fmt.Et(latest, "none"))
            .Add("Last error", state?.LastError is { } err ? $"{err} at {Fmt.Et(state.LastErrorUtc)}" : null);
        return OperationResult.Ok(new
        {
            config.Enabled, config.OwnerUserId, Hosted = hosted, config.EdgarPolling, config.LlmClassify, State = state,
            Today = new { News = news?.Count ?? 0, Edgar = edgar?.Count ?? 0 }, LatestStoredUtc = latest,
        }, block.Render());
    }
}

[Operation("catalyst sources", Summary = "Every IP-T602 intake source (press-release wires, StockTwits, Reddit): on/off, Vault key, last poll and session counts; never shows a key.", Risk = OperationRisk.Read)]
[Example("idiotproof catalyst sources")]
public sealed class CatalystSourcesOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var sourceSettings = ctx.Services.GetService<CatalystSourceSettings>();
        var keys = ctx.Services.GetService<CatalystSourceKeys>();
        var state = ctx.Services.GetService<CatalystSourceState>()?.Snapshot();
        var configs = sourceSettings is null ? null : await sourceSettings.ReadAsync(ctx.CancellationToken);

        var rows = CatalystSources.All.Select(info =>
        {
            var cfg = configs?.GetValueOrDefault(info.Name);
            var missingKey = keys?.Missing(info);
            var snap = state?.FirstOrDefault(s => string.Equals(s.Name, info.Name, StringComparison.OrdinalIgnoreCase));
            var status =
                cfg is null ? "on" + (info.DefaultOn ? "" : " (default)") :
                !cfg.Enabled ? $"off ({info.EnabledKey})" :
                missingKey is { } entry ? $"no key in Vault: {entry}" :
                snap?.Status ?? "on";
            return new
            {
                source = info.Name,
                vendor = info.DisplayName,
                kind = info.Kind.ToString().ToLowerInvariant(),
                enabled = cfg?.Enabled ?? info.DefaultOn,
                status,
                urls = cfg?.Urls ?? info.DefaultUrls,
                vault_entries = info.VaultEntries,
                polls = snap?.Polls ?? 0,
                items_seen = snap?.ItemsSeen ?? 0,
                inserted = snap?.Inserted ?? 0,
                merged = snap?.Merged ?? 0,
                signals = snap?.Signals ?? 0,
                last_poll_et = Fmt.Et(snap?.LastPollUtc, null),
                last_error = snap?.LastError,
            };
        }).ToList();

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("source").Column("kind").Column("status", maxWidth: 48).Column("items").Column("inserted/merged").Column("signals");
        foreach (var r in rows) table.Row(r.source, r.kind, r.status, r.items_seen.ToString(), $"{r.inserted}/{r.merged}", r.signals.ToString());
        var lines = new List<string>
        {
            sourceSettings is null
                ? "Not hosted here: these sources run beside the Monitor leader; this Hub does not host it."
                : "Each source runs only while catalyst.intake.enabled is on and this Hub leads (see `catalyst intake-status`).",
            "",
            table.Render(),
            "",
            "A wire's items become catalyst events (or sightings of a story another source already carries, the \"who knew first\" measurement). Social readings are time series (SocialSignal), never catalysts.",
        };
        return OperationResult.Ok(new { sources = rows }, string.Join("\n", lines));
    }
}

public sealed record CatalystBackfillArgs
{
    [Option("symbols", Description = "Comma-separated symbols to backfill.", Required = true)]
    public string Symbols { get; init; } = "";

    [Option("from", Description = "First day (ET, yyyy-MM-dd).", Required = true)]
    public DateOnly From { get; init; }

    [Option("to", Description = "Last day (ET, yyyy-MM-dd); default today.")]
    public DateOnly? To { get; init; }

    [Option("llm-budget", Description = "At most this many new rows get a Haiku classification (default 0: none).")]
    public int LlmBudget { get; init; }

    [Option("edgar", Description = "Also search EDGAR filings (default on; --no-edgar for news only).")]
    public bool Edgar { get; init; } = true;
}

[Operation("catalyst backfill", Summary = "Fill catalyst events for past dates from Alpaca news (paged) and EDGAR full-text search, through the same pre-classifier and scorer as live intake; rows are marked backfilled.", Risk = OperationRisk.Read,
    Description = "Writes research rows only (no strategy, order or setting changes). Needs an owner for the Alpaca keys. EDGAR calls are rate-limited (120 ms apart), so wide ranges take minutes.")]
[Example("idiotproof catalyst backfill --symbols ABCD,EFGH --from 2026-10-01 --to 2026-10-31")]
[Example("idiotproof catalyst backfill --symbols ABCD --from 2026-11-01 --llm-budget 20")]
public sealed class CatalystBackfillOperation : IOperation<CatalystBackfillArgs>
{
    public async Task<OperationResult> ExecuteAsync(CatalystBackfillArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var symbols = args.Symbols.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        if (symbols.Count == 0) return OperationResult.Usage("--symbols needs at least one symbol.");
        if (symbols.Count > 50) return OperationResult.Usage("At most 50 symbols per backfill.");
        var to = args.To ?? DateOnly.FromDateTime(EasternClock.ToEastern(ctx.UtcNow));
        if (to < args.From) return OperationResult.Usage("--to is before --from.");
        if (args.LlmBudget < 0) return OperationResult.Usage("--llm-budget must be 0 or more.");

        var fromUtc = EasternClock.ToUtcFromEastern(args.From.ToDateTime(TimeOnly.MinValue));
        var toUtc = EasternClock.ToUtcFromEastern(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (toUtc > ctx.UtcNow) toUtc = ctx.UtcNow;

        var s = await ctx.Get<CatalystBackfillService>().BackfillAsync(symbols, fromUtc, toUtc, args.LlmBudget, owner, args.Edgar, ctx.CancellationToken);
        var text = $"Backfilled {string.Join(", ", symbols)} {args.From:yyyy-MM-dd}..{to:yyyy-MM-dd}: " +
                   $"{s.NewsSeen} news item(s) and {s.EdgarSeen} filing(s) seen, {s.Inserted} stored, {s.Duplicates} already present, " +
                   $"{s.NoSymbol} without a symbol, {s.LlmClassified} LLM-classified." +
                   (s.Notes.Count > 0 ? "\n" + string.Join("\n", s.Notes) : "");
        return OperationResult.Ok(new { Symbols = symbols, From = args.From, To = to, s.NewsSeen, s.EdgarSeen, s.Inserted, s.Duplicates, s.NoSymbol, s.LlmClassified, s.Notes }, text);
    }
}

public sealed record CatalystReclassifyArgs
{
    [Option("store", Description = "app (this database's catalyst events), history (the permanent history's news and filings) or all (default).")]
    public string Store { get; init; } = "all";
}

[Operation("catalyst reclassify", Summary = "Write the catalyst type (outside investment / PIPE, acquisition target or acquirer, merger, earnings beat / miss / report, guidance, contract / partnership, FDA / clinical, reverse split, uplisting, listing compliance, buyback, analyst upgrade, offering, theme pivot, other, no catalyst) and the non-binding-LOI flag onto stored catalyst events and the permanent history's news and filings, and report the share left in other / no catalyst against the old Other.", Risk = OperationRisk.Write,
    Description = "Deterministic rules only (headline keywords, form type, 8-K items): no model, no network call. Idempotent: a row whose type already matches is not written. The frozen version 1 class, direction and score are never changed. The gapper study's research rows are typed by `research gappers catalysts`.")]
[Example("idiotproof catalyst reclassify")]
[Example("idiotproof catalyst reclassify --store history")]
public sealed class CatalystReclassifyOperation : IOperation<CatalystReclassifyArgs>
{
    public async Task<OperationResult> ExecuteAsync(CatalystReclassifyArgs args, OperationContext ctx)
    {
        var store = args.Store.Trim().ToLowerInvariant();
        if (store is not ("app" or "history" or "all")) return OperationResult.Usage("--store must be app, history or all.");
        var reclassifier = ctx.Get<CatalystReclassifier>();
        var results = new List<CatalystReclassifyResult>();
        if (store is "app" or "all") results.Add(await reclassifier.AppEventsAsync(ctx.CancellationToken));
        if (store is "history" or "all")
        {
            try
            {
                results.Add(await reclassifier.HistoryNewsAsync(ctx.CancellationToken));
                results.Add(await reclassifier.HistoryFilingsAsync(ctx.CancellationToken));
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                return OperationResult.Blocked($"History database unreachable: {ex.Message}");
            }
        }

        static string Share(long part, long whole) => whole == 0 ? "-" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("store").Column("rows", Align.Right).Column("written", Align.Right).Column("old Other", Align.Right)
            .Column("other", Align.Right).Column("no catalyst", Align.Right).Column("LOI flag", Align.Right).Column("seconds", Align.Right);
        foreach (var r in results)
            table.Row(r.Store, r.Rows.ToString("N0", CultureInfo.InvariantCulture), r.Written.ToString("N0", CultureInfo.InvariantCulture), Share(r.LegacyOther, r.Rows),
                Share(r.Other, r.Rows), Share(r.NoCatalyst, r.Rows), r.NonBindingLoi.ToString("N0", CultureInfo.InvariantCulture), r.Seconds.ToString("0.0", CultureInfo.InvariantCulture));
        var lines = new List<string> { table.Render() };
        foreach (var r in results.Where(r => r.Rows > 0))
            lines.Add($"{r.Store}: " + string.Join(", ", r.TypeCounts.OrderByDescending(t => t.Value).Select(t => $"{t.Key} {t.Value.ToString("N0", CultureInfo.InvariantCulture)} ({Share(t.Value, r.Rows)})")));
        return OperationResult.Ok(results, string.Join("\n", lines));
    }
}
