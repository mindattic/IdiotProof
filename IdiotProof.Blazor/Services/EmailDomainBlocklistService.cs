using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Guards registration against disposable / temporary email domains (IP-A23).
/// The blocklist lives in SQL (<c>DomainNameBlacklist</c>) so it's editable at
/// runtime; this service seeds it from a bundled list at startup (idempotent)
/// and answers <see cref="IsBlockedAsync"/> during registration.
/// </summary>
public sealed class EmailDomainBlocklistService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Extracts the lowercased domain from an email, or null if malformed.</summary>
    public static string? DomainOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return null;
        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        // Must look like a domain: has a dot, no spaces, no trailing dot.
        if (!domain.Contains('.') || domain.Contains(' ') || domain.StartsWith('.') || domain.EndsWith('.'))
            return null;
        return domain;
    }

    /// <summary>True when the email's domain is on the blacklist (or the email is malformed).</summary>
    public async Task<bool> IsBlockedAsync(string? email, CancellationToken ct = default)
    {
        var domain = DomainOf(email);
        if (domain is null) return true; // malformed → reject
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DomainNameBlacklist.AnyAsync(d => d.Domain == domain, ct);
    }

    /// <summary>
    /// Seeds any bundled disposable domains not already present. Idempotent —
    /// safe to run every startup; only inserts the diff.
    /// </summary>
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.DomainNameBlacklist.Select(d => d.Domain).ToListAsync(ct);
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var toAdd = DisposableDomains
            .Where(d => !have.Contains(d))
            .Select(d => new BlockedEmailDomain { Domain = d, Reason = "disposable" })
            .ToList();
        if (toAdd.Count == 0) return 0;
        db.DomainNameBlacklist.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        return toAdd.Count;
    }

    /// <summary>
    /// Known disposable / temporary-inbox providers. Not exhaustive (thousands
    /// exist) but covers the common bot/abuse domains; admins can add more to
    /// the table. All lowercase.
    /// </summary>
    private static readonly string[] DisposableDomains =
    [
        "mailinator.com", "guerrillamail.com", "guerrillamail.info", "guerrillamail.net",
        "guerrillamail.org", "guerrillamail.biz", "sharklasers.com", "grr.la", "guerrillamailblock.com",
        "10minutemail.com", "10minutemail.net", "20minutemail.com", "temp-mail.org", "tempmail.com",
        "tempmail.net", "tempmailo.com", "tempr.email", "tempmailaddress.com", "throwawaymail.com",
        "yopmail.com", "yopmail.net", "yopmail.fr", "cool.fr.nf", "jetable.fr.nf", "nospam.ze.tc",
        "getnada.com", "nada.email", "dispostable.com", "trashmail.com", "trashmail.net", "trash-mail.com",
        "mailnesia.com", "mailcatch.com", "maildrop.cc", "mintemail.com", "mytemp.email", "moakt.com",
        "fakemail.net", "fakeinbox.com", "fake-mail.net", "spamgourmet.com", "mailexpire.com",
        "emailondeck.com", "email-fake.com", "emailfake.com", "tempinbox.com", "inboxbear.com",
        "spam4.me", "burnermail.io", "33mail.com", "anonaddy.com", "mohmal.com", "harakirimail.com",
        "spambox.us", "discard.email", "discardmail.com", "mailde.de", "wegwerfmail.de", "trbvm.com",
        "byom.de", "einrot.com", "gustr.com", "cuvox.de", "dayrep.com", "fleckens.hu", "jourrapide.com",
        "rhyta.com", "superrito.com", "teleworm.us", "armyspy.com", "tempail.com", "luxusmail.org",
        "1secmail.com", "1secmail.org", "1secmail.net", "kzccv.com", "qiott.com", "wuuvo.com",
        "mailpoof.com", "minuteinbox.com", "tmpmail.org", "tmpmail.net", "tmpeml.com", "tmails.net",
        "mail-temp.com", "tempmail.plus", "vomoto.com", "spambog.com", "spambog.de", "spambog.ru",
    ];
}
