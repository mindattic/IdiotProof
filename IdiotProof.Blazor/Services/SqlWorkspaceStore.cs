using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Storage;
using IdiotProof.Engine.Workspace;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// SQL-backed implementation of <see cref="IWorkspaceStore"/>. Replaces the
/// JSON-on-disk default for the Blazor host so user-edited workspaces land in
/// the same database the rest of the user-state lives in.
///
/// Storage shape: each <see cref="WorkspaceTab"/> serializes to opaque JSON in
/// <see cref="Workspace.BodyJson"/> (the entity column, not the DSL workspace),
/// keyed by the tab's <c>TabId</c>. Schema-tolerant by design — adding a new
/// field to <c>WorkspaceTab</c> doesn't require a migration.
///
/// On first construction with an empty Workspaces table, performs a one-shot
/// import from the legacy on-disk JSON files (via a co-built
/// <see cref="JsonFileWorkspaceStore"/>) so users with existing workspaces
/// don't lose data on the cutover. The import is idempotent: subsequent boots
/// see a non-empty SQL table and skip the import.
/// </summary>
public sealed class SqlWorkspaceStore : IWorkspaceStore
{
    private readonly WorkspaceRepository repo;
    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly IStorageProvider storage;
    private readonly object importLock = new();
    private bool importChecked;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqlWorkspaceStore(
        WorkspaceRepository repo,
        IDbContextFactory<AppDbContext> dbFactory,
        IStorageProvider storage)
    {
        this.repo      = repo;
        this.dbFactory = dbFactory;
        this.storage   = storage;
    }

    public IReadOnlyList<WorkspaceTab> Load(string userId)
    {
        EnsureLegacyImported();

        var rows = repo.GetAllForUserAsync(userId).GetAwaiter().GetResult();
        var list = new List<WorkspaceTab>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var tab = JsonSerializer.Deserialize<WorkspaceTab>(row.BodyJson, JsonOptions);
                if (tab != null) list.Add(tab);
            }
            catch
            {
                // Don't crash the page on a bad row — skip, leave the import path
                // available to repair via re-save.
            }
        }
        return list;
    }

    public void Save(string userId, WorkspaceTab tab)
    {
        var json = JsonSerializer.Serialize(tab, JsonOptions);
        repo.UpsertAsync(tab.TabId, userId, tab.Name, json).GetAwaiter().GetResult();
    }

    public bool Delete(string userId, string tabId)
    {
        // The repo deletes by tabId regardless of owner; check ownership first
        // so a leaked tabId can't wipe another user's row.
        var existing = repo.GetByIdAsync(tabId).GetAwaiter().GetResult();
        if (existing is null || existing.OwnerUserId != userId) return false;

        repo.DeleteAsync(tabId).GetAwaiter().GetResult();
        return true;
    }

    public IEnumerable<string> EnumerateUserIds()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Workspaces
            .Select(w => w.OwnerUserId)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// One-shot legacy import. Runs at most once per host process (lock + flag).
    /// If the SQL Workspaces table is empty AND on-disk JSON exists, copies
    /// every legacy file into SQL. After this runs successfully, the disk
    /// files become read-only artifacts — the SQL table is canonical.
    /// </summary>
    private void EnsureLegacyImported()
    {
        if (importChecked) return;
        lock (importLock)
        {
            if (importChecked) return;
            importChecked = true;

            try
            {
                using var db = dbFactory.CreateDbContext();
                if (db.Workspaces.Any()) return; // already imported / SQL is canonical

                // Build a fresh JSON store pointed at the same storage provider so
                // we read existing files without touching live infra.
                var legacy = new JsonFileWorkspaceStore(storage);
                foreach (var userId in legacy.EnumerateUserIds())
                {
                    foreach (var tab in legacy.Load(userId))
                    {
                        var json = JsonSerializer.Serialize(tab, JsonOptions);
                        repo.UpsertAsync(tab.TabId, userId, tab.Name, json).GetAwaiter().GetResult();
                    }
                }
            }
            catch
            {
                // If the import fails (e.g. a corrupt JSON file blocks read), let the
                // user's first save populate SQL. We don't want a botched import
                // path to crash the page.
            }
        }
    }
}
