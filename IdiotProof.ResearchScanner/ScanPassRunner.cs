using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Engine.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IdiotProof.ResearchScanner;

/// <summary>
/// One scan pass: refresh the ticker universe, sweep watchlist tickers every
/// pass plus a rotating batch of the rest, run the regulatory scanner on its
/// own slower cadence, then score everything new. Never lets one bad ticker
/// or source abort the whole pass — every external call is caught and logged
/// so a single flaky API doesn't zero out an otherwise-successful run.
/// </summary>
public sealed class ScanPassRunner(IServiceProvider services, ILogger logger)
{
    private static readonly TimeSpan UniverseMaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan RegulatoryCadence =
        TimeSpan.FromHours(double.TryParse(Environment.GetEnvironmentVariable("IDIOTPROOF_RESEARCHSCAN_REGULATORY_HOURS"), out var h) ? h : 24);
    private const string RegulatoryMarker = "regulatory-scan-ran";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Conservative pacing across hundreds of tickers per pass — nothing hit
    // these APIs at this volume before this scanner existed. EDGAR's fair-
    // access guidance is ~10 req/sec max; Alpaca and Claude get their own
    // smaller margins. CorporateActionDetector's own internal document
    // fetches (0-2 per ticker, only for high-priority 8-Ks) aren't wrapped
    // here — a smaller, bounded residual gap rather than changing its
    // constructor shape for this pass.
    private readonly AsyncThrottle edgarThrottle = new(maxConcurrent: 1, minInterval: TimeSpan.FromMilliseconds(150));
    private readonly AsyncThrottle alpacaThrottle = new(maxConcurrent: 2, minInterval: TimeSpan.FromMilliseconds(150));
    private readonly AsyncThrottle llmThrottle = new(maxConcurrent: 2, minInterval: TimeSpan.FromMilliseconds(250));

    public async Task RunAsync(ScanRun scanRun, CancellationToken ct)
    {
        var batchSize = int.TryParse(Environment.GetEnvironmentVariable("IDIOTPROOF_RESEARCHSCAN_BATCHSIZE"), out var bs) ? bs : 300;
        var daysBack  = int.TryParse(Environment.GetEnvironmentVariable("IDIOTPROOF_RESEARCHSCAN_DAYSBACK"), out var db2) ? db2 : 2;

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var dbFactory   = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var tickerUniv  = sp.GetRequiredService<TickerUniverseService>();
        var edgar       = sp.GetRequiredService<EdgarService>();
        var alpacaNews  = sp.GetRequiredService<AlpacaNewsService>();
        var research    = sp.GetRequiredService<ResearchService>();
        var backfill    = sp.GetRequiredService<OutcomeBackfillService>();
        var form4Parser = sp.GetRequiredService<Form4Parser>();
        var corpActions = sp.GetRequiredService<CorporateActionDetector>();
        var regulatory  = sp.GetRequiredService<RegulatoryScanner>();
        var scorer      = sp.GetRequiredService<SignificanceScorer>();

        await tickerUniv.RefreshIfStaleAsync(UniverseMaxAge, ct);
        var universe  = await tickerUniv.GetUniverseAsync(ct);
        scanRun.UniverseSize = universe.Count;

        var watchlist = await GetWatchlistSymbolsAsync(dbFactory, ct);

        var rest = universe.Select(t => t.Symbol)
            .Except(watchlist, StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var runNumber = await (await dbFactory.CreateDbContextAsync(ct)).ScanRuns.CountAsync(ct);
        var rotatingBatch = TakeRotating(rest, runNumber, batchSize);

        var scanList = watchlist
            .Union(rotatingBatch, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation(
            "Scan pass: {WatchlistCount} watchlist + {BatchCount}/{RestCount} rotating (universe {UniverseSize})",
            watchlist.Count, rotatingBatch.Count, rest.Count, universe.Count);

        var newClaimIds = new List<Guid>();

        foreach (var ticker in scanList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                newClaimIds.AddRange(await ScanTickerAsync(
                    ticker, daysBack, edgar, alpacaNews, research, form4Parser, corpActions, dbFactory,
                    edgarThrottle, alpacaThrottle, llmThrottle, ct));
                scanRun.TickersScanned++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scan failed for {Ticker} — continuing with the rest of the batch", ticker);
                scanRun.ErrorCount++;
            }
        }

        scanRun.ClaimsFound = newClaimIds.Count;

        // Regulatory scan runs on its own, much slower cadence — rule filings
        // are infrequent; no point polling Federal Register every pass.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var lastRegulatory = await db.ScanRuns
                .Where(s => s.Notes != null && s.Notes.Contains(RegulatoryMarker))
                .OrderByDescending(s => s.StartedUtc)
                .Select(s => (DateTime?)s.StartedUtc)
                .FirstOrDefaultAsync(ct);

            if (lastRegulatory is null || DateTime.UtcNow - lastRegulatory.Value >= RegulatoryCadence)
            {
                var since = lastRegulatory ?? DateTime.UtcNow.AddDays(-14);
                var regulatoryCount = await regulatory.ScanAsync(since, ct);
                scanRun.ClaimsFound += regulatoryCount;
                scanRun.Notes = (scanRun.Notes ?? "") + $" {RegulatoryMarker} (+{regulatoryCount})";
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Regulatory scan failed — continuing");
            scanRun.ErrorCount++;
        }

        // Index add/delete log (data/sp-index-events.json) → IndexEvent claims. Cheap file
        // read, so every pass; the scanner itself is idempotent.
        try
        {
            var indexCount = await sp.GetRequiredService<IndexEventScanner>().ScanAsync(ct);
            if (indexCount > 0)
            {
                scanRun.ClaimsFound += indexCount;
                scanRun.Notes = (scanRun.Notes ?? "") + $" index-events (+{indexCount})";
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Index-event scan failed — continuing");
            scanRun.ErrorCount++;
        }

        // Backfill realized price outcomes BEFORE scoring — this is what gives
        // SignificanceScorer's historical-correlation and source-trust bonuses
        // real data to read instead of sitting at zero forever.
        try
        {
            var backfilledCount = await backfill.BackfillAsync(ct: ct);
            if (backfilledCount > 0)
                scanRun.Notes = (scanRun.Notes ?? "") + $" outcomes-backfilled ({backfilledCount})";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outcome backfill failed — continuing");
            scanRun.ErrorCount++;
        }

        // Score every claim still missing a score — not just this pass's IDs,
        // so a claim left unscored by a prior crash or a failed scoring pass
        // (including regulatory/macro claims, whose IDs ScanAsync doesn't
        // return) gets picked up here instead of staying stuck forever.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            const int unscoredCap = 2000;
            var unscoredIds = await db.ResearchClaims
                .Where(c => c.SignificanceScore == null)
                .OrderByDescending(c => c.CreatedUtc)
                .Take(unscoredCap)
                .Select(c => c.Id)
                .ToListAsync(ct);

            if (unscoredIds.Count == unscoredCap)
                logger.LogWarning("Unscored-claims backlog exceeds {Cap} — scoring the newest {Cap} this pass, rest deferred", unscoredCap, unscoredCap);

            if (unscoredIds.Count > 0)
                await scorer.ScoreAndPersistBatchAsync(unscoredIds, watchlist, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Significance scoring failed for this pass's claims");
            scanRun.ErrorCount++;
        }
    }

    private static async Task<List<Guid>> ScanTickerAsync(
        string ticker, int daysBack,
        EdgarService edgar, AlpacaNewsService alpacaNews, ResearchService research,
        Form4Parser form4Parser, CorporateActionDetector corpActions,
        IDbContextFactory<AppDbContext> dbFactory,
        AsyncThrottle edgarThrottle, AsyncThrottle alpacaThrottle, AsyncThrottle llmThrottle,
        CancellationToken ct)
    {
        var claimIds = new List<Guid>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Alpaca news
        var articles = await alpacaThrottle.RunAsync(() => alpacaNews.GetNewsAsync(ticker, daysBack, ct), ct);
        foreach (var article in articles)
        {
            var text = string.IsNullOrWhiteSpace(article.Summary) ? article.Headline : $"{article.Headline}\n\n{article.Summary}";
            var claims = await llmThrottle.RunAsync(() => research.AnalyzeArticleAsync(
                ticker, text, article.Source, article.Url, sourceTier: 2,
                DateOnly.FromDateTime(article.PublishedAt), ct), ct);
            claimIds.AddRange(claims.Select(c => c.Id));
        }

        // 8-K material events — CorporateActionDetector decides which are
        // worth a real document fetch and supplies the fallback boilerplate
        // for the rest.
        var eightKs = await edgarThrottle.RunAsync(() => edgar.GetRecentFilingsAsync(ticker, "8-K", daysBack, ct), ct);
        foreach (var result in await corpActions.DetectAsync(eightKs, ct))
        {
            var articleDate = DateOnly.TryParse(result.Filing.FilingDate, out var d) ? d : today;
            var claims = await llmThrottle.RunAsync(() => research.AnalyzeArticleAsync(
                ticker, result.Text, "SEC EDGAR 8-K", result.Filing.BrowseUrl, sourceTier: 1, articleDate, ct), ct);
            claimIds.AddRange(claims.Select(c => c.Id));
        }

        // Form 4 insider transactions — real share/dollar magnitude, not
        // boilerplate. One claim per filing; InsiderTransaction rows link to it.
        var form4s = await edgarThrottle.RunAsync(() => edgar.GetRecentFilingsAsync(ticker, "4", daysBack, ct), ct);
        foreach (var filing in form4s)
        {
            var articleDate = DateOnly.TryParse(filing.FilingDate, out var d) ? d : today;
            var xml = await edgarThrottle.RunAsync(() => edgar.GetFilingDocumentAsync(filing, ct), ct);

            string text;
            List<InsiderTransaction> transactions = [];
            if (xml is not null)
            {
                transactions = form4Parser.Parse(xml, filing.BrowseUrl);
                text = transactions.Count > 0
                    ? string.Join(" ", transactions.Select(form4Parser.Summarize))
                    : $"SEC Form 4 (insider transaction) filed {filing.FilingDate} by {filing.EntityName}.";
            }
            else
            {
                text = $"SEC Form 4 (insider transaction) filed {filing.FilingDate} by {filing.EntityName}. " +
                       "A director, officer, or 10%+ shareholder changed their beneficial ownership.";
            }

            var claims = await llmThrottle.RunAsync(() => research.AnalyzeArticleAsync(
                ticker, text, "SEC EDGAR Form 4", filing.BrowseUrl, sourceTier: 1, articleDate, ct), ct);
            claimIds.AddRange(claims.Select(c => c.Id));

            if (claims.Count > 0 && transactions.Count > 0)
                await PersistInsiderTransactionsAsync(transactions, claims[0].Id, dbFactory, ct);
        }

        return claimIds;
    }

    private static async Task PersistInsiderTransactionsAsync(
        List<InsiderTransaction> transactions, Guid claimId,
        IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var t in transactions)
        {
            t.ClaimId = claimId;
            db.InsiderTransactions.Add(t);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<string>> GetWatchlistSymbolsAsync(
        IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Workspaces.Select(w => w.BodyJson).ToListAsync(ct);

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in rows)
        {
            try
            {
                var tab = JsonSerializer.Deserialize<WorkspaceTab>(json, JsonOpts);
                if (tab?.Watchlist is null) continue;
                foreach (var s in tab.Watchlist)
                    if (!string.IsNullOrWhiteSpace(s)) symbols.Add(s.Trim().ToUpperInvariant());
            }
            catch (JsonException) { /* corrupt row — skip, matches SqlWorkspaceStore's tolerance */ }
        }
        return symbols.ToList();
    }

    private static List<string> TakeRotating(List<string> ordered, int runNumber, int batchSize)
    {
        if (ordered.Count == 0 || batchSize <= 0) return [];
        if (ordered.Count <= batchSize) return ordered;

        var start = (runNumber * batchSize) % ordered.Count;
        var batch = new List<string>(batchSize);
        for (var i = 0; i < batchSize; i++)
            batch.Add(ordered[(start + i) % ordered.Count]);
        return batch;
    }
}
