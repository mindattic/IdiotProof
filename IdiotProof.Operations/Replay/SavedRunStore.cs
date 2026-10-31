using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace IdiotProof.Operations.Replay;

/// <summary>
/// Keeps the results of simulation runs that a later command refers back to by id —
/// <c>gapper backtest-day</c> → <c>gapper apply-tuned &lt;id&gt;</c>, <c>backtest run</c> →
/// <c>backtest conditions &lt;id&gt;</c>. One JSON file per run under a per-owner folder, so a run
/// is only ever visible to the owner who made it and survives a Hub restart. Ids look like
/// <c>gb-1a2b3c4d</c> (a kind prefix plus eight hex digits) and are validated before they touch
/// the file system. Hosts and tests may register their own instance; otherwise
/// <see cref="Default"/> (under %LOCALAPPDATA%\IdiotProof\runs) is used.
/// </summary>
public sealed partial class SavedRunStore(string root)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>The store under the current user's local application data.</summary>
    public static SavedRunStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdiotProof", "runs"));

    /// <summary>The store registered for this call, else <see cref="Default"/>.</summary>
    public static SavedRunStore For(OperationContext ctx) => ctx.Services.GetService<SavedRunStore>() ?? Default;

    /// <summary>The folder runs are written under.</summary>
    public string Root { get; } = root;

    /// <summary>Saves <paramref name="payload"/> and returns its new id (<c>{kind}-{8 hex}</c>).</summary>
    public async Task<string> SaveAsync<T>(string kind, Guid owner, T payload, DateTime createdUtc, CancellationToken ct = default)
    {
        if (!KindRegex().IsMatch(kind)) throw new ArgumentException($"Run kind '{kind}' must be 1-8 lowercase letters.", nameof(kind));
        var dir = Path.Combine(Root, owner.ToString("N"));
        Directory.CreateDirectory(dir);
        string id;
        do id = $"{kind}-{Guid.NewGuid().ToString("N")[..8]}";
        while (File.Exists(Path.Combine(dir, id + ".json")));

        var envelope = new Envelope<T>(id, kind, createdUtc, payload);
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"), JsonSerializer.Serialize(envelope, JsonOpts), ct);
        return id;
    }

    /// <summary>
    /// The run with this id, when it exists, belongs to <paramref name="owner"/> and is of
    /// <paramref name="kind"/>; otherwise null.
    /// </summary>
    public async Task<SavedRun<T>?> LoadAsync<T>(string id, string kind, Guid owner, CancellationToken ct = default)
    {
        var clean = (id ?? "").Trim().ToLowerInvariant();
        if (!IdRegex().IsMatch(clean) || !clean.StartsWith(kind + "-", StringComparison.Ordinal)) return null;
        var path = Path.Combine(Root, owner.ToString("N"), clean + ".json");
        if (!File.Exists(path)) return null;
        var envelope = JsonSerializer.Deserialize<Envelope<T>>(await File.ReadAllTextAsync(path, ct), JsonOpts);
        return envelope is null || envelope.Payload is null ? null : new SavedRun<T>(envelope.Id, envelope.CreatedUtc, envelope.Payload);
    }

    private sealed record Envelope<T>(string Id, string Kind, DateTime CreatedUtc, T Payload);

    [GeneratedRegex("^[a-z]{1,8}$")]
    private static partial Regex KindRegex();

    [GeneratedRegex("^[a-z]{1,8}-[0-9a-f]{8}$")]
    private static partial Regex IdRegex();
}

/// <summary>A run read back from <see cref="SavedRunStore"/>.</summary>
public sealed record SavedRun<T>(string Id, DateTime CreatedUtc, T Payload);
