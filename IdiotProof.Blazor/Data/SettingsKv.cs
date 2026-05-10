using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Generic key-value table for runtime-editable settings. Replaces the
/// per-process AppSettings disk file as the source of truth for settings the
/// user can change in-app (theme defaults, evaluation interval, LLM voting
/// threshold, etc.). The AppSettings class still serves as the typed snapshot
/// loaded at startup; SettingsRepository reads/writes the canonical KV here
/// and overlays into AppSettings on the same chain that env vars + Legion
/// credentials use.
///
/// Why a KV instead of a typed Settings table: the schema changes faster than
/// the code can migrate. New runtime knobs land as a new key, no migration
/// required. The trade-off (loose typing) is acceptable for low-volume reads
/// at startup; hot-path values still live on the typed AppSettings.
/// </summary>
public sealed class SettingsKv
{
    /// <summary>
    /// Setting key. Convention: dot-namespaced, e.g. "engine.evaluationIntervalSeconds",
    /// "llm.consensusThreshold", "ui.defaultTheme". Lowercase, no spaces.
    /// </summary>
    [Key, MaxLength(128)]
    public string Key { get; set; } = "";

    /// <summary>
    /// Raw value. Caller is responsible for type coercion. Booleans → "true"/"false";
    /// numbers → invariant culture; complex objects → JSON.
    /// </summary>
    [Required]
    public string Value { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
