using System.Threading.Channels;
using IdiotProof.Core.Data;
using IdiotProof.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// The LLM half of the intake (IP-T443): a bounded queue of event ids that Haiku (through
/// MindAttic.Legion, via <see cref="CatalystExtractor"/>) classifies in the background, writing the
/// Llm* columns and re-scoring. Gated by <c>catalyst.intake.llmClassify</c> and capped per ET day by
/// <c>catalyst.intake.llmDailyBudget</c> (IP-T447); the backfill calls
/// <see cref="ClassifyAsync"/> directly within its budget. Headline and body are untrusted input and
/// travel in the user message only.
/// </summary>
public sealed class CatalystClassifier(
    IServiceScopeFactory scopes,
    IDbContextFactory<AppDbContext> dbFactory,
    CatalystIntakeSettings intakeSettings,
    TimeProvider time,
    ILogger<CatalystClassifier> logger) : BackgroundService
{
    public const int Capacity = 2000;

    private readonly Channel<Guid> queue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });
    private long dropped;
    private long classified;
    private long failed;
    private long overBudget;
    private readonly Lock budgetGate = new();
    private DateTime budgetDay;
    private int usedToday;

    public CatalystScoringWeights Weights { get; init; } = CatalystCatalogs.ScoringWeights;

    public int Pending => queue.Reader.Count;

    public long Dropped => Interlocked.Read(ref dropped);

    public long Classified => Interlocked.Read(ref classified);

    public long Failed => Interlocked.Read(ref failed);

    /// <summary>Queued ids skipped because today's (ET) live call budget was spent.</summary>
    public long OverBudget => Interlocked.Read(ref overBudget);

    /// <summary>Live calls attempted so far today (ET), seeded at day start from today's live classified rows so a restart does not reset it.</summary>
    public int UsedToday
    {
        get { lock (budgetGate) return budgetDay == EasternTime.FromUtc(time.GetUtcNow().UtcDateTime).Date ? usedToday : 0; }
    }

    /// <summary>
    /// Takes one call from the ET day's live budget (IP-T447). Every attempt counts, answered or not,
    /// since a failed parse still spent the tokens; a rejected call (no credit) is cheap but counted alike
    /// so a dead key cannot spin through the whole queue.
    /// </summary>
    private async Task<bool> TryTakeBudgetAsync(int budget, CancellationToken ct)
    {
        if (budget <= 0) return false;
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var day = EasternTime.FromUtc(nowUtc).Date;
        bool seeded;
        lock (budgetGate) seeded = budgetDay == day;
        if (!seeded)
        {
            var dayStartUtc = EasternTime.ToUtc(day);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var already = await db.CatalystEvents.AsNoTracking()
                .CountAsync(e => !e.Backfilled && e.LlmClassifiedUtc != null && e.LlmClassifiedUtc >= dayStartUtc, ct);
            lock (budgetGate)
            {
                if (budgetDay != day) { budgetDay = day; usedToday = already; }
            }
        }
        lock (budgetGate)
        {
            if (usedToday >= budget) return false;
            usedToday++;
            return true;
        }
    }

    public bool Enqueue(Guid eventId)
    {
        if (queue.Writer.TryWrite(eventId)) return true;
        Interlocked.Increment(ref dropped);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var config = await intakeSettings.ReadAsync(stoppingToken);
                    if (!config.LlmClassify) continue;
                    if (!await TryTakeBudgetAsync(config.LlmDailyBudget, stoppingToken))
                    {
                        // Left unclassified; the backfill (its own budget) or a later run can pick it up.
                        Interlocked.Increment(ref overBudget);
                        continue;
                    }
                    await ClassifyAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    logger.LogWarning(ex, "Catalyst LLM classification failed for {Id}", id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Classifies one stored event now; false when it is missing, already classified, or no key/answer resolved.</summary>
    public async Task<bool> ClassifyAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.CatalystEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (row is null || row.LlmClassifiedUtc is not null) return false;

        using var scope = scopes.CreateScope();
        var extractor = scope.ServiceProvider.GetRequiredService<CatalystExtractor>();
        var text = string.IsNullOrWhiteSpace(row.RawSnippet) ? row.Headline : $"{row.Headline}\n\n{row.RawSnippet}";
        var source = row.Source == CatalystIntake.Edgar ? $"SEC EDGAR {row.FormType}" : "Alpaca news";
        var extraction = await extractor.ExtractAsync(row.PrimarySymbol, text, source, ct);
        if (extraction is null) return false;

        var now = time.GetUtcNow().UtcDateTime;
        var top = extraction.Catalysts.OrderBy(c => MagnitudeRank(c.Magnitude)).FirstOrDefault();
        row.LlmType = top?.Type ?? "None";
        row.LlmSentiment = top?.Sentiment ?? "Neutral";
        row.LlmMagnitude = top?.Magnitude ?? "Low";
        row.LlmHasHappened = top?.HasHappenedAlready;
        row.LlmMechanism = top?.Mechanism is { Length: > 0 } m ? (m.Length > 500 ? m[..500] : m) : null;
        row.LlmClassifiedUtc = now;

        var direction = row.LlmSentiment switch { "Bullish" => 1, "Bearish" => -1, _ => row.PreDirection };
        var preClass = Enum.TryParse<CatalystLegacyClass>(row.PreClass, out var pc) ? pc : CatalystLegacyClass.Other;
        // Freshness at receipt, as the ingestor scores it: aging by the classification time decayed every
        // backfilled (and queued) row to the 0.05 floor, so history and live rows stopped being comparable.
        var age = Math.Max(0, (row.ReceivedUtc - row.PublishedUtc).TotalMinutes);
        (row.CatalystScore, row.Direction) = CatalystScorer.Score(Weights, preClass, direction, row.LlmMagnitude, row.SourceTier, row.NoveltyCount, age);
        row.ScoreComputedUtc = now;
        await db.SaveChangesAsync(ct);
        Interlocked.Increment(ref classified);
        return true;
    }

    private static int MagnitudeRank(string magnitude) => magnitude switch { "High" => 0, "Medium" => 1, _ => 2 };
}
