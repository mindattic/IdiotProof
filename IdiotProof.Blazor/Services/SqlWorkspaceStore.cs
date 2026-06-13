using System.Text.Json;
using IdiotProof.Blazor.Data;
using IdiotProof.Engine.Workspace;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// SQL-backed <see cref="IWorkspaceStore"/> that stores each <see cref="WorkspaceTab"/>
/// as a JSON blob in the <c>Workspaces</c> table. Registered in the Blazor host before
/// <c>AddIdiotProofEngine</c> so it wins over the JSON-on-disk default.
///
/// On first load for a user, if the SQL table is empty but the legacy JSON directory
/// has files, a one-shot import copies them into SQL so existing workspaces survive
/// the storage migration transparently.
/// </summary>
internal sealed class SqlWorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly IDbContextFactory<AppDbContext> dbFactory;
    private readonly JsonFileWorkspaceStore jsonFallback;

    public SqlWorkspaceStore(
        IDbContextFactory<AppDbContext> dbFactory,
        IdiotProof.Engine.Storage.IStorageProvider storage)
    {
        this.dbFactory = dbFactory;
        jsonFallback = new JsonFileWorkspaceStore(storage);
    }

    public IReadOnlyList<WorkspaceTab> Load(string userId)
    {
        if (!Guid.TryParse(userId, out var uid)) return [];
        using var db = dbFactory.CreateDbContext();
        var rows = db.Workspaces
            .Where(w => w.OwnerUserId == uid)
            .OrderBy(w => w.UpdatedUtc)
            .ToList();

        if (rows.Count == 0)
        {
            var imported = ImportFromJson(userId, uid, db);
            if (imported.Count > 0) return imported;
        }

        return rows
            .Select(r => JsonSerializer.Deserialize<WorkspaceTab>(r.BodyJson, jsonOpts)!)
            .Where(t => t is not null)
            .ToList();
    }

    public void Save(string userId, WorkspaceTab tab)
    {
        if (!Guid.TryParse(userId, out var uid)) return;
        using var db = dbFactory.CreateDbContext();
        var existing = db.Workspaces.Find(tab.TabId);
        var json = JsonSerializer.Serialize(tab, jsonOpts);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            db.Workspaces.Add(new WorkspaceRow
            {
                WorkspaceId = tab.TabId,
                OwnerUserId = uid,
                Name = tab.Name,
                BodyJson = json,
                CreatedUtc = now,
                UpdatedUtc = now,
            });
        }
        else
        {
            existing.Name = tab.Name;
            existing.BodyJson = json;
            existing.UpdatedUtc = now;
        }

        db.SaveChanges();
    }

    public bool Delete(string userId, string tabId)
    {
        if (!Guid.TryParse(userId, out var uid)) return false;
        using var db = dbFactory.CreateDbContext();
        var row = db.Workspaces.FirstOrDefault(w => w.WorkspaceId == tabId && w.OwnerUserId == uid);
        if (row is null) return false;
        db.Workspaces.Remove(row);
        db.SaveChanges();
        return true;
    }

    public IEnumerable<string> EnumerateUserIds()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Workspaces
            .Select(w => w.OwnerUserId)
            .Distinct()
            .ToList()
            .Select(id => id.ToString());
    }

    private List<WorkspaceTab> ImportFromJson(string userId, Guid uid, AppDbContext db)
    {
        IReadOnlyList<WorkspaceTab> fromJson;
        try { fromJson = jsonFallback.Load(userId); }
        catch { return []; }

        if (fromJson.Count == 0) return [];

        var now = DateTime.UtcNow;
        foreach (var tab in fromJson)
        {
            db.Workspaces.Add(new WorkspaceRow
            {
                WorkspaceId = tab.TabId,
                OwnerUserId = uid,
                Name = tab.Name,
                BodyJson = JsonSerializer.Serialize(tab, jsonOpts),
                CreatedUtc = now,
                UpdatedUtc = now,
            });
        }
        db.SaveChanges();

        return fromJson.ToList();
    }
}
