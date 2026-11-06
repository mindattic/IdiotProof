using IdiotProof.Core.Services;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

public sealed record CatalystBackfillSummary(int NewsSeen, int EdgarSeen, int Inserted, int Duplicates, int NoSymbol, int LlmClassified, List<string> Notes);

/// <summary>
/// Fills <see cref="Data.CatalystEvent"/> for past dates (IP-T443): Alpaca news by date range with
/// paging and EDGAR full-text search by explicit date range, through the same ingestor, pre-classifier
/// and scorer as live intake, so history and live rows are comparable. Rows carry
/// <c>Backfilled = true</c> and <c>ReceivedUtc = PublishedUtc</c> (no latency is known). LLM
/// classification stops at the budget.
/// </summary>
public sealed class CatalystBackfillService(
    AlpacaNewsService alpacaNews,
    EdgarService edgar,
    EdgarFilingHeaders headers,
    CatalystIngestor ingestor,
    CatalystClassifier classifier,
    UserKeyService userKeys,
    ILogger<CatalystBackfillService> logger)
{
    public const string EdgarForms = "8-K,4,S-1,S-3,424B3,424B4,424B5";

    public async Task<CatalystBackfillSummary> BackfillAsync(IReadOnlyList<string> symbols, DateTime fromUtc, DateTime toUtc,
        int llmBudget, Guid? ownerUserId = null, bool includeEdgar = true, CancellationToken ct = default)
    {
        var syms = symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        var notes = new List<string>();
        int newsSeen = 0, edgarSeen = 0, inserted = 0, duplicates = 0, noSymbol = 0;
        var insertedIds = new List<Guid>();

        (string KeyId, string Secret)? keys = null;
        if (ownerUserId is { } owner)
        {
            var k = await userKeys.GetOrCreateAsync(owner, ct);
            if (!string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) && !string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey))
                keys = (k.AlpacaApiKeyId, k.AlpacaApiSecretKey);
        }

        await foreach (var item in alpacaNews.GetNewsRangeAsync(syms, fromUtc, toUtc, keys: keys, ct: ct))
        {
            newsSeen++;
            var ordered = item.Symbols.OrderBy(s => syms.Contains(s) ? 0 : 1).ToList();
            var intake = new CatalystIntake(CatalystIntake.AlpacaNews, item.Id, item.CreatedAt, item.CreatedAt, ordered,
                item.Headline, item.Summary, item.Url, item.Source, Backfilled: true);
            Count(await ingestor.IngestAsync(intake, ct));
        }

        if (includeEdgar)
        {
            var from = DateOnly.FromDateTime(fromUtc);
            var to = DateOnly.FromDateTime(toUtc);
            foreach (var symbol in syms)
            {
                foreach (var filing in await edgar.GetFilingsAsync(symbol, EdgarForms, from, to, ct))
                {
                    edgarSeen++;
                    var header = filing.IssuerCik is null ? null : await headers.FetchAsync(filing.IssuerCik, filing.AccessionNumber, ct);
                    var published = header?.AcceptanceUtc
                        ?? (DateTime.TryParse(filing.FilingDate, out var fd) ? EasternTime.ToUtc(fd.Date.AddHours(6)) : fromUtc);
                    if (published < fromUtc || published > toUtc) continue;
                    var items = filing.Items is { Length: > 0 } i ? i : header?.Items is { Count: > 0 } h ? h.ToArray() : null;
                    var entry = new EdgarAtomEntry(filing.FormType, filing.EntityName, filing.IssuerCik ?? "", "", filing.AccessionNumber, filing.BrowseUrl, null, "");
                    var intake = new CatalystIntake(CatalystIntake.Edgar, filing.AccessionNumber, published, published, [symbol],
                        EdgarLatestFilingsPoller.Headline(entry, items), null, filing.BrowseUrl, "edgar", filing.FormType, items, Backfilled: true);
                    Count(await ingestor.IngestAsync(intake, ct));
                }
            }
        }

        var classified = 0;
        foreach (var id in insertedIds.Take(Math.Max(0, llmBudget)))
        {
            try { if (await classifier.ClassifyAsync(id, ct)) classified++; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Backfill LLM classification failed for {Id}", id); }
        }
        if (llmBudget > 0 && insertedIds.Count > llmBudget) notes.Add($"{insertedIds.Count - llmBudget} new row(s) left unclassified (budget {llmBudget}).");
        if (keys is null && ownerUserId is not null) notes.Add("owner has no Alpaca data keys; news pulled with the Hub's keys.");
        return new CatalystBackfillSummary(newsSeen, edgarSeen, inserted, duplicates, noSymbol, classified, notes);

        void Count(CatalystIngestResult r)
        {
            switch (r.Outcome)
            {
                case CatalystIngestOutcome.Inserted: inserted++; insertedIds.Add(r.Id!.Value); break;
                case CatalystIngestOutcome.Duplicate: duplicates++; break;
                default: noSymbol++; break;
            }
        }
    }
}
