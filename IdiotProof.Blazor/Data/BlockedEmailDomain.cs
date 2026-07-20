using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// A disposable / temporary email domain that is barred from registration
/// (IP-A23). Real money rides on real accounts — throwaway inboxes used by bots
/// and abuse (mailinator, guerrillamail, 10minutemail, …) never get an account.
/// Runtime-editable blocklist → SQL (IP-LAW-7); seeded from a bundled list at
/// startup and extendable by an admin later.
/// </summary>
[Table("DomainNameBlacklist")]
public sealed class BlockedEmailDomain
{
    /// <summary>The domain, stored lowercase (e.g. "mailinator.com"). Unique key.</summary>
    [Key, MaxLength(255)]
    public string Domain { get; set; } = "";

    /// <summary>Why it's blocked (e.g. "disposable").</summary>
    [MaxLength(64)]
    public string Reason { get; set; } = "disposable";

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}
