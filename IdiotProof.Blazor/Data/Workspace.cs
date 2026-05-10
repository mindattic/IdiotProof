using System.ComponentModel.DataAnnotations;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// User-owned workspace — the container for a watchlist + a set of strategies
/// running together. Migrated from the legacy on-disk JSON
/// (<c>%LOCALAPPDATA%\MindAttic\IdiotProof\Workspaces\*.json</c>) so all
/// user-editable state lives in SQL.
///
/// The full workspace shape (Watchlist, Strategies array, risk params, AutoTrade
/// flag, broker/feed overrides) is serialized into <see cref="BodyJson"/> rather
/// than fanned out into separate columns. This keeps schema migration cost zero
/// when the WorkspaceTab DTO grows new fields — the engine deserializes the
/// blob on read, applies defaults for missing fields, and rewrites on save.
/// Top-level columns capture only what the Strategies-list view needs to render
/// without deserializing the body.
/// </summary>
public sealed class Workspace
{
    /// <summary>Stable ID — same value as the legacy JSON file's WorkspaceTab.TabId.</summary>
    [Key, MaxLength(64)]
    public string WorkspaceId { get; set; } = "";

    /// <summary>FK to AspNetUsers.Id. Indexed; cascade-delete with the user.</summary>
    [Required, MaxLength(450)]
    public string OwnerUserId { get; set; } = "";

    /// <summary>Display name shown on the Strategies / Workspaces lists.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>
    /// Serialized WorkspaceTab JSON. Schema-tolerant: deserialization fills
    /// missing fields with defaults so adding a new field doesn't require a
    /// data migration.
    /// </summary>
    [Required]
    public string BodyJson { get; set; } = "{}";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
