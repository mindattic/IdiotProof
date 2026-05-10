using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Per-user UI state. Canonical source is SQL; mirrored to localStorage for SSR
/// pre-paint flash protection (theme) and offline read (open tabs, last selected
/// account). One row per AppUser.
/// </summary>
public sealed class UserPreferences
{
    [Key, MaxLength(450)]
    public string UserId { get; set; } = "";

    /// <summary>Active theme. Defaults to "alpaca". Future: "dark", "high-contrast".</summary>
    [MaxLength(64)]
    public string Theme { get; set; } = "alpaca";

    /// <summary>
    /// Active Alpaca account id (e.g. "PA3PG9SP46WU" for paper, "252673877" for live).
    /// Drives which broker client receives orders + which positions render. Empty = paper default.
    /// </summary>
    [MaxLength(64)]
    public string ActiveAccountId { get; set; } = "";

    /// <summary>"paper" or "live". Tracks which account-type pill is selected.</summary>
    [MaxLength(16)]
    public string ActiveAccountType { get; set; } = "paper";

    /// <summary>Comma-separated strategy guids the user has open as tabs in the editor.</summary>
    public string OpenStrategyTabs { get; set; } = "";

    /// <summary>JSON blob of free-form UI state — sidebar collapse, expanded rows, scroll positions.</summary>
    public string UiStateJson { get; set; } = "{}";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
