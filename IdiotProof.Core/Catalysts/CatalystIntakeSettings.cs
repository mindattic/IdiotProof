using IdiotProof.Core.Services;

namespace IdiotProof.Core.Catalysts;

/// <summary>The intake's SettingsKv switches as one snapshot.</summary>
public sealed record CatalystIntakeConfig(bool Enabled, Guid? OwnerUserId, bool EdgarPolling, bool LlmClassify);

/// <summary>
/// Reads the <c>catalyst.intake.*</c> settings (pattern of <see cref="Monitoring.ExhaustionScanner"/>'s keys),
/// cached briefly so the news stream's per-message checks never become a per-message SQL read.
/// </summary>
public sealed class CatalystIntakeSettings(SettingsRepository settings, TimeProvider time)
{
    public const string EnabledKey = "catalyst.intake.enabled";
    public const string OwnerKey = "catalyst.intake.ownerUserId";
    public const string EdgarPollingKey = "catalyst.intake.edgarPolling";
    public const string LlmClassifyKey = "catalyst.intake.llmClassify";

    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private readonly Lock gate = new();
    private CatalystIntakeConfig? cached;
    private DateTime cachedAtUtc;

    public async Task<CatalystIntakeConfig> ReadAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (gate)
        {
            if (cached is { } c && now - cachedAtUtc < Ttl) return c;
        }
        var fresh = new CatalystIntakeConfig(
            await settings.GetBoolAsync(EnabledKey, ct) ?? false,
            Guid.TryParse(await settings.GetAsync(OwnerKey, ct), out var owner) ? owner : null,
            await settings.GetBoolAsync(EdgarPollingKey, ct) ?? false,
            await settings.GetBoolAsync(LlmClassifyKey, ct) ?? false);
        lock (gate)
        {
            cached = fresh;
            cachedAtUtc = now;
        }
        return fresh;
    }

    public void Invalidate()
    {
        lock (gate) cached = null;
    }
}
