using System.Globalization;
using IdiotProof.Core.Hosting;
using IdiotProof.Engine.Storage;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
using IdiotProof.Core.Monitoring;
namespace IdiotProof.Operations.Monitor;

/// <summary>One parsed line of the SupervisedLoop heartbeat file (<c>Logs\monitor.heartbeat</c>).</summary>
public sealed record MonitorHeartbeat
{
    public required DateTime WrittenUtc { get; init; }
    public DateTime? TickStartUtc { get; init; }
    public required string Status { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Parses <c>2026-10-30T14:00:00.0000000Z\ttickStart=…\tstatus=ok\tconsecutiveFailures=0[\terror=…]</c>;
    /// null when the text is not a heartbeat.
    /// </summary>
    public static MonitorHeartbeat? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('\t');
        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var written))
            return null;
        var fields = parts.Skip(1)
            .Select(p => p.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.OrdinalIgnoreCase);
        return new MonitorHeartbeat
        {
            WrittenUtc = written,
            TickStartUtc = fields.TryGetValue("tickStart", out var ts) && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start) ? start : null,
            Status = fields.GetValueOrDefault("status") ?? "?",
            ConsecutiveFailures = int.TryParse(fields.GetValueOrDefault("consecutiveFailures"), out var n) ? n : 0,
            Error = fields.GetValueOrDefault("error"),
        };
    }
}

/// <summary>The one-word verdict <c>monitor status</c> leads with.</summary>
public enum MonitorHealth
{
    /// <summary>This Hub was started with IDIOTPROOF_HUB_MONITOR=0.</summary>
    NotHosted,
    /// <summary>Hosted, but waiting for another process to release the leader lease.</summary>
    Standby,
    /// <summary>Hosted, holds the lease, ticking on time.</summary>
    Running,
    /// <summary>Holds the lease but the pause flag is set: nothing is evaluated, entries or exits.</summary>
    Paused,
    /// <summary>Ticking, but the last ticks failed (backing off).</summary>
    Failing,
    /// <summary>No tick for far longer than the interval: the loop is stuck or dead.</summary>
    Stale,
    /// <summary>The loop has ended (shutting down).</summary>
    Stopped,
}

/// <summary>Shared reads behind the <c>monitor</c> operations (IP-T256..T258).</summary>
public static class MonitorReads
{
    /// <summary>A tick older than this many intervals (and at least <see cref="MinStale"/>) is stale.</summary>
    public const int StaleIntervals = 10;

    public static readonly TimeSpan MinStale = TimeSpan.FromSeconds(30);

    /// <summary>The hosted Monitor's live state, or null when this Hub does not host it.</summary>
    public static MonitorRuntimeSnapshot? Runtime(OperationContext ctx) =>
        ctx.Services.GetService<MonitorRuntimeState>()?.Snapshot();

    /// <summary>The pause flag (the Monitor's own instance when hosted, else one over the same SettingsKv row).</summary>
    public static MonitorPauseFlag PauseFlag(OperationContext ctx) =>
        ctx.Services.GetService<MonitorPauseFlag>() ?? new MonitorPauseFlag(ctx.Get<SettingsRepository>(), ctx.Clock);

    /// <summary>Who holds the leader lease; a probe failure comes back as Note, never an exception.</summary>
    public static async Task<MonitorLeaseInfo> LeaseAsync(OperationContext ctx)
    {
        var probe = ctx.Services.GetService<IMonitorLeaseProbe>()
                    ?? (ctx.Services.GetService<IdiotProofDatabase>() is { } db ? new SqlMonitorLeaseProbe(db) : null);
        if (probe is null) return new MonitorLeaseInfo { Held = false, Note = "no database configured" };
        try { return await probe.ProbeAsync(ctx.CancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MonitorLeaseInfo { Held = false, Note = $"lease probe failed: {ex.Message}" };
        }
    }

    /// <summary>The heartbeat file's path, or null when no storage provider is registered.</summary>
    public static string? HeartbeatPath(OperationContext ctx) =>
        ctx.Services.GetService<IStorageProvider>() is { } storage ? MonitorWorker.HeartbeatPathFor(storage) : null;

    /// <summary>The last heartbeat, or null when there is no file yet.</summary>
    public static MonitorHeartbeat? Heartbeat(OperationContext ctx)
    {
        var path = HeartbeatPath(ctx);
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return MonitorHeartbeat.Parse(new StreamReader(stream).ReadToEnd());
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>How long without a tick before the loop counts as stale.</summary>
    public static TimeSpan StaleAfter(MonitorRuntimeSnapshot? runtime)
    {
        var interval = TimeSpan.FromSeconds(runtime?.IntervalSeconds is > 0 ? runtime.IntervalSeconds : 1);
        var limit = interval * StaleIntervals;
        return limit < MinStale ? MinStale : limit;
    }

    /// <summary>The verdict from what the process knows (runtime state + the pause flag).</summary>
    public static MonitorHealth Classify(MonitorRuntimeSnapshot? runtime, bool paused, DateTime nowUtc)
    {
        if (runtime is null) return MonitorHealth.NotHosted;
        if (!runtime.Running) return MonitorHealth.Stopped;
        if (!runtime.LeaseHeld) return MonitorHealth.Standby;
        var last = runtime.LastTickUtc ?? runtime.LeaseAcquiredUtc ?? runtime.StartedUtc;
        if (last is { } t && nowUtc - t > StaleAfter(runtime)) return MonitorHealth.Stale;
        if (paused) return MonitorHealth.Paused;
        if (runtime.ConsecutiveFailures > 0) return MonitorHealth.Failing;
        return MonitorHealth.Running;
    }

    /// <summary>"3s ago", "4m 10s ago", "2h 5m ago".</summary>
    public static string Ago(DateTime? utc, DateTime nowUtc)
    {
        if (utc is not { } t) return "never";
        var d = nowUtc - t;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalSeconds < 90) return $"{(int)d.TotalSeconds}s ago";
        if (d.TotalMinutes < 90) return $"{(int)d.TotalMinutes}m {d.Seconds}s ago";
        if (d.TotalHours < 48) return $"{(int)d.TotalHours}h {d.Minutes}m ago";
        return $"{(int)d.TotalDays}d {d.Hours}h ago";
    }

    /// <summary>"healthy", "paused", … as shown to people.</summary>
    public static string Word(MonitorHealth health) => health switch
    {
        MonitorHealth.NotHosted => "not hosted (this Hub runs with IDIOTPROOF_HUB_MONITOR=0)",
        MonitorHealth.Standby => "standby (another process holds the leader lease)",
        MonitorHealth.Running => "running",
        MonitorHealth.Paused => "PAUSED (no entries, no exits) - `idiotproof monitor resume`",
        MonitorHealth.Failing => "failing (ticks erroring, backing off)",
        MonitorHealth.Stale => "STALE (no tick recently - the loop may be stuck)",
        MonitorHealth.Stopped => "stopped",
        _ => health.ToString(),
    };
}
