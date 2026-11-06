using System.Globalization;
using IdiotProof.Core.Services;

namespace IdiotProof.Core.Monitoring.Formation;

/// <summary>The recorder's SettingsKv switches as one snapshot. Prices and caps are USD; times are ET.</summary>
public sealed record FormationConfig
{
    public bool Enabled { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string BrokerMode { get; init; } = "paper";
    public int TickSeconds { get; init; } = 15;
    public double PriceMin { get; init; } = 0.10;
    public double PriceMax { get; init; } = 20.0;
    public double? MarketCapMin { get; init; }
    public double? MarketCapMax { get; init; }
    public int MaxSymbols { get; init; } = 150;
    public int EveryMinutes { get; init; } = 5;
    public double MinGap { get; init; } = 10;
    public double MinVolRatio { get; init; } = 3;
    public int ControlEveryMinutes { get; init; } = 15;
    public int ControlOneIn { get; init; } = 10;
    public string? TodProfilePath { get; init; }
    public IReadOnlyList<double> TodDecileEdges { get; init; } = [];
    public int RetentionDays { get; init; } = 30;
    public bool ArmingEnabled { get; init; }
    public TimeSpan? ScheduledAutoEt { get; init; }

    public bool IsPaper => string.Equals(BrokerMode.Trim(), "paper", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the <c>formation.*</c> keys (pattern of <see cref="ExhaustionScanner"/>'s keys), cached
/// briefly so a 15-second tick never becomes twenty SQL reads.
/// </summary>
public sealed class FormationSettings(SettingsRepository settings, TimeProvider time)
{
    public const string EnabledKey = "formation.enabled";
    public const string OwnerKey = "formation.ownerUserId";
    public const string BrokerModeKey = "formation.brokerMode";
    public const string TickSecondsKey = "formation.tickSeconds";
    public const string PriceMinKey = "formation.universe.priceMin";
    public const string PriceMaxKey = "formation.universe.priceMax";
    public const string MarketCapMinKey = "formation.universe.marketCapMin";
    public const string MarketCapMaxKey = "formation.universe.marketCapMax";
    public const string MaxSymbolsKey = "formation.universe.maxSymbols";
    public const string EveryMinutesKey = "formation.record.everyMinutes";
    public const string MinGapKey = "formation.record.minGap";
    public const string MinVolRatioKey = "formation.record.minVolRatio";
    public const string ControlEveryMinutesKey = "formation.record.controlEveryMinutes";
    public const string ControlOneInKey = "formation.record.controlOneIn";
    public const string TodProfilePathKey = "formation.todProfilePath";
    public const string TodDecileEdgesKey = "formation.todDecileEdges";
    public const string RetentionDaysKey = "formation.retentionDays";
    public const string ArmingEnabledKey = "formation.arming.enabled";
    public const string ScheduledAutoEtKey = "gapper.auto.scheduledEt";

    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private readonly Lock gate = new();
    private FormationConfig? cached;
    private DateTime cachedAtUtc;

    public async Task<FormationConfig> ReadAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (gate)
        {
            if (cached is { } c && now - cachedAtUtc < Ttl) return c;
        }
        var defaults = new FormationConfig();
        var fresh = new FormationConfig
        {
            Enabled = await settings.GetBoolAsync(EnabledKey, ct) ?? false,
            OwnerUserId = Guid.TryParse(await settings.GetAsync(OwnerKey, ct), out var owner) ? owner : null,
            BrokerMode = (await settings.GetAsync(BrokerModeKey, ct) ?? defaults.BrokerMode).Trim().ToLowerInvariant(),
            TickSeconds = Math.Clamp(await settings.GetIntAsync(TickSecondsKey, ct) ?? defaults.TickSeconds, 5, 300),
            PriceMin = await Double(PriceMinKey, defaults.PriceMin, ct),
            PriceMax = await Double(PriceMaxKey, defaults.PriceMax, ct),
            MarketCapMin = await NullableDouble(MarketCapMinKey, ct),
            MarketCapMax = await NullableDouble(MarketCapMaxKey, ct),
            MaxSymbols = Math.Clamp(await settings.GetIntAsync(MaxSymbolsKey, ct) ?? defaults.MaxSymbols, 1, 2000),
            EveryMinutes = Math.Clamp(await settings.GetIntAsync(EveryMinutesKey, ct) ?? defaults.EveryMinutes, 1, 60),
            MinGap = await Double(MinGapKey, defaults.MinGap, ct),
            MinVolRatio = await Double(MinVolRatioKey, defaults.MinVolRatio, ct),
            ControlEveryMinutes = Math.Clamp(await settings.GetIntAsync(ControlEveryMinutesKey, ct) ?? defaults.ControlEveryMinutes, 1, 120),
            ControlOneIn = Math.Clamp(await settings.GetIntAsync(ControlOneInKey, ct) ?? defaults.ControlOneIn, 1, 1000),
            TodProfilePath = NullIfBlank(await settings.GetAsync(TodProfilePathKey, ct)),
            TodDecileEdges = ParseEdges(await settings.GetAsync(TodDecileEdgesKey, ct)),
            RetentionDays = Math.Clamp(await settings.GetIntAsync(RetentionDaysKey, ct) ?? defaults.RetentionDays, 1, 3650),
            ArmingEnabled = await settings.GetBoolAsync(ArmingEnabledKey, ct) ?? false,
            ScheduledAutoEt = ParseTime(await settings.GetAsync(ScheduledAutoEtKey, ct)),
        };
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

    private async Task<double> Double(string key, double fallback, CancellationToken ct) =>
        (double?)await settings.GetDecimalAsync(key, ct) ?? fallback;

    private async Task<double?> NullableDouble(string key, CancellationToken ct) =>
        (double?)await settings.GetDecimalAsync(key, ct);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static TimeSpan? ParseTime(string? text) =>
        !string.IsNullOrWhiteSpace(text) && TimeSpan.TryParseExact(text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : null;

    internal static IReadOnlyList<double> ParseEdges(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var edges = new List<double>();
        foreach (var part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) edges.Add(v);
        edges.Sort();
        return edges;
    }
}
