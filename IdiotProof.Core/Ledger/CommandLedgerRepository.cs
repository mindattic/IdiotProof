using IdiotProof.Core.Data;
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
            await LinkAuditRowsAsync(db, entry, ct);
            return entry.Id;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Command ledger write failed for {Operation} (continuing).", entry.Operation);
            return null;
        }
    }

    /// <summary>
    /// Stamps this ledger row's id onto the audit rows the operation's gateway calls wrote
    /// (<see cref="CommandScope"/>): their JSON gains <c>"command_ledger_id": id</c>, so an order's
    /// audit trail names the command that caused it. Only the scope for this same operation is
    /// linked, and only once. Best effort: a failure here never fails the ledger write.
    /// </summary>
    private async Task LinkAuditRowsAsync(AppDbContext db, CommandLedgerEntry entry, CancellationToken ct)
    {
        var scope = CommandScope.Current;
        if (scope is null || scope.LedgerId is not null || scope.AuditIds.Count == 0) return;
        if (!string.Equals(scope.Operation.Length > 64 ? scope.Operation[..64] : scope.Operation, entry.Operation, StringComparison.Ordinal)) return;
        try
        {
            var ids = scope.AuditIds.ToList();
            var rows = await db.AuditLogs.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
            foreach (var row in rows) row.DataJson = WithLedgerId(row.DataJson, entry.Id);
            await db.SaveChangesAsync(ct);
            scope.LedgerId = entry.Id;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Linking audit rows to ledger row {Id} failed (continuing).", entry.Id);
        }
    }

    /// <summary>Adds <c>command_ledger_id</c> to a JSON object (or wraps non-JSON text so it is kept).</summary>
    public static string WithLedgerId(string? json, long ledgerId)
    {
        var trimmed = json?.TrimStart();
        if (trimmed is null || trimmed.Length == 0) return $"{{\"command_ledger_id\":{ledgerId}}}";
        if (trimmed[0] == '{')
            return trimmed.Length > 1 && trimmed[1..].TrimStart().StartsWith('}')
                ? $"{{\"command_ledger_id\":{ledgerId}}}"
                : $"{{\"command_ledger_id\":{ledgerId}," + trimmed[1..];
        return $"{{\"command_ledger_id\":{ledgerId},\"text\":{System.Text.Json.JsonSerializer.Serialize(json)}}}";
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
