using System.Text.Json;
using System.Text.Json.Serialization;
using IdiotProof.MAUI.Models;

namespace IdiotProof.MAUI.Services;

/// <summary>
/// CRUD for ScriptStrategy files. Stored as JSON in app data.
/// </summary>
public sealed class StrategyFileService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _storageDir;

    public StrategyFileService()
    {
        _storageDir = Path.Combine(FileSystem.AppDataDirectory, "Strategies");
        Directory.CreateDirectory(_storageDir);
    }

    private string FilePath(string id) => Path.Combine(_storageDir, $"{id}.json");

    public async Task<List<ScriptStrategy>> GetAllAsync()
    {
        var strategies = new List<ScriptStrategy>();
        if (!Directory.Exists(_storageDir)) return strategies;

        foreach (var file in Directory.GetFiles(_storageDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var strategy = JsonSerializer.Deserialize<ScriptStrategy>(json, JsonOpts);
                if (strategy is not null)
                    strategies.Add(strategy);
            }
            catch
            {
                // Skip corrupted files
            }
        }

        return strategies.OrderByDescending(s => s.ModifiedUtc).ToList();
    }

    public async Task<ScriptStrategy?> LoadAsync(string id)
    {
        var path = FilePath(id);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<ScriptStrategy>(json, JsonOpts);
    }

    public async Task SaveAsync(ScriptStrategy strategy)
    {
        strategy.ModifiedUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(strategy, JsonOpts);
        await File.WriteAllTextAsync(FilePath(strategy.Id), json);
    }

    public async Task<ScriptStrategy> CreateAsync(string name = "Untitled Strategy")
    {
        var strategy = new ScriptStrategy { Name = name };

        // Seed with a Ticker segment so the grid isn't empty
        strategy.Segments.Add(SegmentCatalog.Create("Ticker"));

        await SaveAsync(strategy);
        return strategy;
    }

    public async Task RenameAsync(string id, string newName)
    {
        var strategy = await LoadAsync(id);
        if (strategy is null) return;
        strategy.Name = newName;
        await SaveAsync(strategy);
    }

    public Task DeleteAsync(string id)
    {
        var path = FilePath(id);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<ScriptStrategy> DuplicateAsync(string id)
    {
        var source = await LoadAsync(id);
        if (source is null) throw new InvalidOperationException($"Strategy {id} not found");

        var clone = JsonSerializer.Deserialize<ScriptStrategy>(
            JsonSerializer.Serialize(source, JsonOpts), JsonOpts)!;

        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = $"{source.Name} (Copy)";
        clone.CreatedUtc = DateTime.UtcNow;

        await SaveAsync(clone);
        return clone;
    }
}
