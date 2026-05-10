using IdiotProof.Blazor.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// SQL-backed workspace storage. Replaces the legacy
/// <c>%LOCALAPPDATA%\MindAttic\IdiotProof\Workspaces\*.json</c> files for new
/// installs; the Engine's <c>WorkspaceManager</c> can adopt this in a future
/// pass without breaking existing on-disk data (a one-shot disk→SQL importer
/// is the natural follow-on).
///
/// The workspace body is stored as opaque JSON in <see cref="Workspace.BodyJson"/>
/// — schema-tolerant by design. The DTO shape (Watchlist, Strategies, etc.)
/// lives in <c>IdiotProof.Engine</c>; this repo doesn't crack the blob, leaving
/// type-safe deserialization to the consumer.
/// </summary>
public sealed class WorkspaceRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Workspace>> GetAllForUserAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Workspaces
            .Where(w => w.OwnerUserId == userId)
            .OrderByDescending(w => w.UpdatedUtc)
            .ToListAsync(ct);
    }

    public async Task<Workspace?> GetByIdAsync(string workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Workspaces.FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct);
    }

    public async Task UpsertAsync(string workspaceId, string ownerUserId, string name, string bodyJson, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Workspaces.FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            db.Workspaces.Add(new Workspace
            {
                WorkspaceId = workspaceId,
                OwnerUserId = ownerUserId,
                Name        = name,
                BodyJson    = bodyJson,
                CreatedUtc  = now,
                UpdatedUtc  = now,
            });
        }
        else
        {
            row.Name        = name;
            row.BodyJson    = bodyJson;
            row.UpdatedUtc  = now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Workspaces.FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct);
        if (row is null) return;
        db.Workspaces.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
