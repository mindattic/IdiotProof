using IdiotProof.Core.Services;

namespace IdiotProof.Core.Catalysts;

/// <summary>The intake's SettingsKv switches as one snapshot.</summary>
/// <remarks>
/// <see cref="LlmDailyBudget"/> caps the live queue's Haiku calls per ET day (IP-T447); 0 or less means no
/// live calls at all. The backfill keeps its own per-run budget and does not count against it.
/// </remarks>
public sealed record CatalystIntakeConfig(bool Enabled, Guid? OwnerUserId, bool EdgarPolling, bool LlmClassify,
    int LlmDailyBudget = CatalystIntakeSettings.DefaultLlmDailyBudget);

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
    public const string LlmDailyBudgetKey = "catalyst.intake.llmDailyBudget";

    /// <summary>Live Haiku calls per ET day when <see cref="LlmDailyBudgetKey"/> is unset (about $2.50 a day at Haiku 4.5 prices).</summary>
    public const int DefaultLlmDailyBudget = 500;

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
            await settings.GetBoolAsync(LlmClassifyKey, ct) ?? false,
            await settings.GetIntAsync(LlmDailyBudgetKey, ct) ?? DefaultLlmDailyBudget);
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
