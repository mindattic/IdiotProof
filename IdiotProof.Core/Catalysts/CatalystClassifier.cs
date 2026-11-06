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
/// Llm* columns and re-scoring. Gated by <c>catalyst.intake.llmClassify</c>; the backfill calls
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

    public CatalystScoringWeights Weights { get; init; } = CatalystCatalogs.ScoringWeights;

    public int Pending => queue.Reader.Count;

    public long Dropped => Interlocked.Read(ref dropped);

    public long Classified => Interlocked.Read(ref classified);

    public long Failed => Interlocked.Read(ref failed);

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
                    if (!(await intakeSettings.ReadAsync(stoppingToken)).LlmClassify) continue;
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
        var preClass = Enum.TryParse<CatalystPreClass>(row.PreClass, out var pc) ? pc : CatalystPreClass.Other;
        var age = Math.Max(0, (now - row.PublishedUtc).TotalMinutes);
        (row.CatalystScore, row.Direction) = CatalystScorer.Score(Weights, preClass, direction, row.LlmMagnitude, row.SourceTier, row.NoveltyCount, age);
        row.ScoreComputedUtc = now;
        await db.SaveChangesAsync(ct);
        Interlocked.Increment(ref classified);
        return true;
    }

    private static int MagnitudeRank(string magnitude) => magnitude switch { "High" => 0, "Medium" => 1, _ => 2 };
}
