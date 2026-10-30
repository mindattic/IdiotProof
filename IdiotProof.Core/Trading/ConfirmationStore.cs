using System.Security.Cryptography;
using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Trading;

/// <summary>Why a code did not unlock anything. Each maps to a message the user can act on.</summary>
public enum ConfirmationFailure { NotFound, Expired, AlreadyUsed, WrongOwner, Tampered }

/// <summary>The outcome of presenting a code: the stored payload to execute, or why not.</summary>
public sealed record ConfirmationRedemption(PendingConfirmation? Confirmation, ConfirmationFailure? Failure)
{
    public bool Succeeded => Confirmation is not null;

    public string Message => Failure switch
    {
        null => "Confirmed.",
        ConfirmationFailure.NotFound => "No pending confirmation has that code.",
        ConfirmationFailure.Expired => "That code expired — preview the action again for a fresh code.",
        ConfirmationFailure.AlreadyUsed => "That code was already used; a code confirms exactly one action.",
        ConfirmationFailure.WrongOwner => "That code belongs to a different user.",
        ConfirmationFailure.Tampered => "The stored action no longer matches what was previewed; nothing was executed.",
        _ => "Confirmation refused.",
    };
}

/// <summary>
/// The human-in-the-loop gate for real money from the CLI and MCP. A Live action is first
/// previewed: the store keeps the exact payload and returns a short code. Executing needs a
/// second call with that code, by the same owner, within <see cref="Lifetime"/>; the code works
/// once and replays the stored payload, so nothing can be swapped in between preview and confirm.
/// </summary>
public sealed class ConfirmationStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);

    /// <summary>No 0/O, 1/I/L — codes are read off a terminal and typed or spoken back.</summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 6;

    public const string KindOrder = "order";
    public const string KindActivateLive = "activate-live";

    public async Task<PendingConfirmation> IssueAsync(
        Guid ownerUserId, string kind, string payloadJson, string payloadHash, string? preview, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;

        // A code is unique among the codes still usable; expired/used codes may recur later.
        string code;
        do code = NewCode();
        while (await db.PendingConfirmations.AnyAsync(p => p.Code == code && p.ConsumedUtc == null && p.ExpiresUtc > now, ct));

        var row = new PendingConfirmation
        {
            Code = code, OwnerUserId = ownerUserId, Kind = kind,
            PayloadJson = payloadJson, PayloadHash = payloadHash,
            Preview = preview is { Length: > 2048 } ? preview[..2045] + "..." : preview,
            CreatedUtc = now, ExpiresUtc = now + Lifetime,
        };
        db.PendingConfirmations.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>
    /// Marks the code used and returns its payload — exactly once. <paramref name="rehash"/>
    /// recomputes the payload's hash so a row edited after issue is refused, not executed.
    /// </summary>
    public async Task<ConfirmationRedemption> RedeemAsync(
        string code, Guid ownerUserId, string consumedBy, Func<PendingConfirmation, string> rehash, CancellationToken ct = default)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;

        var candidates = await db.PendingConfirmations
            .Where(p => p.Code == normalized)
            .OrderByDescending(p => p.CreatedUtc)
            .ToListAsync(ct);
        if (candidates.Count == 0) return new(null, ConfirmationFailure.NotFound);

        var mine = candidates.Where(p => p.OwnerUserId == ownerUserId).ToList();
        if (mine.Count == 0) return new(null, ConfirmationFailure.WrongOwner);

        var row = mine.FirstOrDefault(p => p.ConsumedUtc == null && p.ExpiresUtc > now);
        if (row is null)
            return new(null, mine.Any(p => p.ConsumedUtc != null) ? ConfirmationFailure.AlreadyUsed : ConfirmationFailure.Expired);

        if (!string.Equals(rehash(row), row.PayloadHash, StringComparison.OrdinalIgnoreCase))
            return new(null, ConfirmationFailure.Tampered);

        row.ConsumedUtc = now;
        row.ConsumedBy = consumedBy;
        try
        {
            // ConsumedUtc is a concurrency token: the UPDATE only matches while it is still NULL,
            // so of two simultaneous confirms exactly one succeeds.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(null, ConfirmationFailure.AlreadyUsed);
        }
        return new(row, null);
    }

    /// <summary>The owner's codes that can still be used, newest first.</summary>
    public async Task<IReadOnlyList<PendingConfirmation>> ListActiveAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.PendingConfirmations.AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId && p.ConsumedUtc == null && p.ExpiresUtc > now)
            .OrderByDescending(p => p.CreatedUtc)
            .ToListAsync(ct);
    }

    private static string NewCode() => string.Create(CodeLength, 0, (span, _) =>
    {
        for (var i = 0; i < span.Length; i++)
            span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    });
}
