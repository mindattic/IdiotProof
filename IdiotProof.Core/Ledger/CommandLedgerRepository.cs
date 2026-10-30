using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Ledger;

/// <summary>
/// Appends and reads <see cref="CommandLedgerEntry"/> rows. Arguments are redacted here, on the
/// way in, so no caller can forget to; a failed write is logged and swallowed — the ledger is a
/// record of what happened, never a reason for an operation to fail.
/// </summary>
public sealed class CommandLedgerRepository(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<CommandLedgerRepository> logger)
{
    /// <summary>Upper bound on stored argument JSON; transcripts can be long and the ledger is not an archive.</summary>
    public const int MaxArgsLength = 16_384;

    public async Task<long?> AppendAsync(CommandLedgerEntry entry, CancellationToken ct = default)
    {
        try
        {
            var args = SecretRedactor.RedactJson(entry.ArgsJson);
            entry.ArgsJson = args is { Length: > MaxArgsLength } ? args[..MaxArgsLength] + "…(truncated)" : args;
            if (entry.Message is { Length: > 1024 }) entry.Message = entry.Message[..1021] + "...";

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.CommandLedger.Add(entry);
            await db.SaveChangesAsync(ct);
            return entry.Id;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Command ledger write failed for {Operation} (continuing).", entry.Operation);
            return null;
        }
    }

    /// <summary>Newest first, optionally for one owner / one operation / since a time.</summary>
    public async Task<IReadOnlyList<CommandLedgerEntry>> ListAsync(
        Guid? ownerUserId = null, string? operation = null, DateTime? sinceUtc = null, int take = 50,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.CommandLedger.AsNoTracking().AsQueryable();
        if (ownerUserId is { } owner) q = q.Where(x => x.OwnerUserId == owner);
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(x => x.Operation == operation);
        if (sinceUtc is { } since) q = q.Where(x => x.TimestampUtc >= since);
        return await q.OrderByDescending(x => x.TimestampUtc).ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
    }
}
