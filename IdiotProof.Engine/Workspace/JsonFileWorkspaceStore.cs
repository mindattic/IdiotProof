using System.Text.Json;
using IdiotProof.Engine.Storage;

namespace IdiotProof.Engine.Workspace;

/// <summary>
/// On-disk JSON implementation of <see cref="IWorkspaceStore"/>. Preserves the
/// original Workspaces/{userId}/{tabId}.json layout so older installs continue
/// reading their files unchanged. Default registration when the host doesn't
/// override (CLI, tests).
///
/// The legacy global-bucket (no userId) lives at <c>WorkspacesPath</c> root —
/// flat files instead of a per-user subfolder. This store treats
/// <c>"__global__"</c> as the marker for that bucket.
/// </summary>
public sealed class JsonFileWorkspaceStore(IStorageProvider storage) : IWorkspaceStore
{
    private const string GlobalBucket = "__global__";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<WorkspaceTab> Load(string userId)
    {
        var dir = ResolveDir(userId);
        if (!Directory.Exists(dir)) return [];

        var list = new List<WorkspaceTab>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var tab = JsonSerializer.Deserialize<WorkspaceTab>(json, JsonOptions);
                if (tab != null) list.Add(tab);
            }
            catch { /* skip corrupt files — better to drop one tab than crash the page */ }
        }
        list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        return list;
    }

    public void Save(string userId, WorkspaceTab tab)
    {
        var dir = ResolveDir(userId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{tab.TabId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(tab, JsonOptions));
    }

    public bool Delete(string userId, string tabId)
    {
        var dir = ResolveDir(userId);
        var path = Path.Combine(dir, $"{tabId}.json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public IEnumerable<string> EnumerateUserIds()
    {
        var root = storage.WorkspacesPath;
        if (!Directory.Exists(root)) yield break;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && name != GlobalBucket) yield return name;
        }
    }

    private string ResolveDir(string userId) => userId == GlobalBucket
        ? storage.WorkspacesPath
        : Path.Combine(storage.WorkspacesPath, userId);
}
