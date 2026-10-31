using IdiotProof.Blazor.Services;

namespace IdiotProof.Monitor;

/// <summary>
/// The Monitor's pause switch (IP-T80): one SettingsKv row, <see cref="PausedKey"/> = "true" or
/// "false", read at the top of every tick. While it is "true" the tick does nothing at all — no
/// scans, no entries and NO exit management for open positions (flatten first, or resume) — and
/// the loop, the heartbeat and the leader lease keep running so resume is instant. Because the flag
/// lives in SQL, <c>idiotproof monitor pause</c> reaches the Monitor wherever it is hosted.
/// <para>
/// Reads are cached for <see cref="CacheFor"/> so a 1-second tick does not query SQL every pass;
/// a write through this class updates the cache at once.
/// </para>
/// </summary>
public sealed class MonitorPauseFlag(SettingsRepository settings, TimeProvider? clock = null)
{
    /// <summary>SettingsKv key: "true" pauses the Monitor.</summary>
    public const string PausedKey = "monitor.paused";

    /// <summary>SettingsKv key: who paused or resumed it last, and why (free text).</summary>
    public const string ReasonKey = "monitor.pausedReason";

    /// <summary>How long a read is reused before SQL is asked again.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private bool cachedPaused;
    private DateTime cachedAtUtc = DateTime.MinValue;

    /// <summary>
    /// True when the Monitor should skip this tick. A SQL failure reads as "not paused" — the
    /// flag can stop trading, but an outage reading it must not silently stop exit management.
    /// </summary>
    public async Task<bool> IsPausedAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (now - cachedAtUtc < CacheFor) return cachedPaused;
        try
        {
            cachedPaused = await settings.GetBoolAsync(PausedKey, ct) ?? false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cachedPaused = false;
        }
        cachedAtUtc = now;
        return cachedPaused;
    }

    /// <summary>The stored state, uncached, with the last reason ("" when none).</summary>
    public async Task<(bool Paused, string Reason)> ReadAsync(CancellationToken ct = default)
    {
        var paused = await settings.GetBoolAsync(PausedKey, ct) ?? false;
        var reason = await settings.GetAsync(ReasonKey, ct) ?? "";
        return (paused, reason);
    }

    /// <summary>Sets the flag; the hosted Monitor honors it on its next tick (within <see cref="CacheFor"/>).</summary>
    public async Task SetAsync(bool paused, string reason, CancellationToken ct = default)
    {
        await settings.SetAsync(PausedKey, paused ? "true" : "false", ct);
        await settings.SetAsync(ReasonKey, reason, ct);
        cachedPaused = paused;
        cachedAtUtc = clock.GetUtcNow().UtcDateTime;
    }
}
