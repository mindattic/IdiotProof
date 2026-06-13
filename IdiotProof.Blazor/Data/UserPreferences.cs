using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Per-user UI state. Canonical source is SQL; mirrored to localStorage for SSR
/// pre-paint flash protection (theme) and offline read (open tabs, last selected
/// account). One row per AppUser.
/// </summary>
public sealed class UserPreferences
{
    [Key]
    public Guid UserId { get; set; }

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

    // ── Risk Guardian config (per-user) ─────────────────────────────────
    // The Monitor instantiates a RiskGuardian per user from these values
    // when evaluating that user's strategies. Defaults match the canonical
    // safe-defaults baked into RiskGuardianConfig — small accounts first,
    // upgrade explicitly via the Settings page.

    /// <summary>Absolute max loss per single trade. Non-negotiable upper bound.</summary>
    public decimal RiskMaxLossPerTrade { get; set; } = 100m;

    /// <summary>Absolute max loss per trading day. Daily circuit breaker.</summary>
    public decimal RiskMaxLossPerDay { get; set; } = 500m;

    /// <summary>Minimum stop distance as % of entry. Prevents micro-stops triggered by noise.</summary>
    public decimal RiskMinStopLossPercent { get; set; } = 0.5m;

    /// <summary>Maximum stop distance as % of entry. Prevents ridiculously wide stops.</summary>
    public decimal RiskMaxStopLossPercent { get; set; } = 5m;

    /// <summary>Account balance the position-size math sizes against.</summary>
    public decimal RiskAccountBalance { get; set; } = 10_000m;

    /// <summary>Maximum % of account to risk per trade.</summary>
    public decimal RiskMaxAccountRiskPercent { get; set; } = 1m;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
