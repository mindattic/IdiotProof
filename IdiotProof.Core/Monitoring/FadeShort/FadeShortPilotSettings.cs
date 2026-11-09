using IdiotProof.Core.Services;

namespace IdiotProof.Core.Monitoring.FadeShort;

/// <summary>The borrow pilot's SettingsKv switches as one snapshot (IP-T480).</summary>
public sealed record FadeShortPilotConfig
{
    public bool Enabled { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string BrokerMode { get; init; } = "paper";
    public string Feed { get; init; } = "sip";
    public int MaxCandidates { get; init; } = FadeShortPilotSettings.DefaultMaxCandidates;
    public string? RegShoDir { get; init; }

    public bool IsPaper => string.Equals(BrokerMode.Trim(), "paper", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the <c>fadeShort.pilot.*</c> keys (pattern of <see cref="ExhaustionScanner"/>'s keys). Off unless
/// <c>fadeShort.pilot.enabled</c> is "true"; the owner comes from <c>fadeShort.pilot.ownerUserId</c>; the
/// pilot refuses to run unless <c>fadeShort.pilot.brokerMode</c> is paper (the default).
/// </summary>
public sealed class FadeShortPilotSettings(SettingsRepository settings)
{
    public const string EnabledKey = "fadeShort.pilot.enabled";
    public const string OwnerKey = "fadeShort.pilot.ownerUserId";
    public const string BrokerModeKey = "fadeShort.pilot.brokerMode";
    public const string FeedKey = "fadeShort.pilot.feed";
    public const string MaxCandidatesKey = "fadeShort.pilot.maxCandidates";
    public const string RegShoDirKey = "fadeShort.pilot.regShoDir";
    public const int DefaultMaxCandidates = 60;

    public async Task<FadeShortPilotConfig> ReadAsync(CancellationToken ct = default)
    {
        var defaults = new FadeShortPilotConfig();
        return new FadeShortPilotConfig
        {
            Enabled = await settings.GetBoolAsync(EnabledKey, ct) ?? false,
            OwnerUserId = Guid.TryParse(await settings.GetAsync(OwnerKey, ct), out var owner) ? owner : null,
            BrokerMode = (await settings.GetAsync(BrokerModeKey, ct) ?? defaults.BrokerMode).Trim().ToLowerInvariant(),
            Feed = NullIfBlank(await settings.GetAsync(FeedKey, ct))?.ToLowerInvariant()
                   ?? NullIfBlank(Environment.GetEnvironmentVariable("IDIOTPROOF_ALPACA_FEED"))?.ToLowerInvariant()
                   ?? defaults.Feed,
            MaxCandidates = Math.Clamp(await settings.GetIntAsync(MaxCandidatesKey, ct) ?? defaults.MaxCandidates, 1, 200),
            RegShoDir = NullIfBlank(await settings.GetAsync(RegShoDirKey, ct)),
        };
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
