using System.Globalization;

using IdiotProof.Core.Data;
namespace IdiotProof.Core.Services;

/// <summary>The value type a <see cref="SettingsKv"/> key holds.</summary>
public enum SettingKind
{
    Int,
    Decimal,
    Bool,
    Text,
    Json,
    Timestamp,
}

/// <summary>
/// One known <c>SettingsKv</c> key: what it means, its type, range, default, and whether a person
/// may change it (internal keys are bookkeeping the app writes for itself). A key ending in
/// <c>.*</c> is a family (<c>decay.be-bex.disarmed.&lt;strategy id&gt;</c>).
/// </summary>
public sealed record SettingDefinition(
    string Key, SettingKind Kind, string Description, string? Default = null,
    decimal? Min = null, decimal? Max = null, bool Internal = false)
{
    public bool Matches(string key) =>
        Key.EndsWith(".*", StringComparison.Ordinal)
            ? key.StartsWith(Key[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(Key, key, StringComparison.OrdinalIgnoreCase);

    /// <summary>The value normalized for storage, or the reason it is not acceptable.</summary>
    public (string? Value, string? Error) Validate(string raw)
    {
        var v = raw.Trim();
        switch (Kind)
        {
            case SettingKind.Int:
                if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return (null, $"{Key} takes a whole number.");
                return RangeError(i) is { } ie ? (null, ie) : (i.ToString(CultureInfo.InvariantCulture), null);
            case SettingKind.Decimal:
                if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return (null, $"{Key} takes a number (invariant culture, e.g. 0.045).");
                return RangeError(d) is { } de ? (null, de) : (d.ToString(CultureInfo.InvariantCulture), null);
            case SettingKind.Bool:
                return v.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "on" => ("true", null),
                    "false" or "0" or "no" or "off" => ("false", null),
                    _ => (null, $"{Key} takes true or false."),
                };
            default:
                return (v, null);
        }
    }

    private string? RangeError(decimal value) =>
        (Min is { } min && value < min) || (Max is { } max && value > max)
            ? $"{Key} must be between {Min?.ToString(CultureInfo.InvariantCulture) ?? "-inf"} and {Max?.ToString(CultureInfo.InvariantCulture) ?? "inf"}."
            : null;
}

/// <summary>
/// The registry of runtime settings stored in <c>SettingsKv</c> (IP-T250): every key the code
/// reads, typed and ranged, so <c>idiotproof settings set</c> can refuse a typo or a nonsense
/// value instead of writing something the reader silently ignores. Add a row here when a new
/// <see cref="SettingsRepository"/> key is introduced.
/// </summary>
public static class SettingsMetadata
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(OptionsTradingService.RiskFreeRateKey, SettingKind.Decimal,
            "Annual risk-free rate for options greeks and implied volatility (0.04 = 4%).",
            OptionsTradingService.DefaultRiskFreeRate.ToString(CultureInfo.InvariantCulture), Min: 0m, Max: 0.25m),
        new("decay.be-bex.lookbackdays", SettingKind.Int,
            "BE/BEX decay scanner: trading days of history compared.", "20", Min: 1m, Max: 250m),
        new("decay.be-bex.threshold", SettingKind.Decimal,
            "BE/BEX decay scanner: decay percent at which the BE long is disarmed.", "8", Min: 0.1m, Max: 100m),
        new(IdiotProof.Core.History.HistoryBackfillWorker.EnabledKey, SettingKind.Bool,
            "Permanent market history (IP-T454): the Hub worker backfills every newly met ticker (bars, news, splits, EDGAR filings) and runs the nightly top-up and market-wide splits/filings pass. Leader-only; writes only the history database.", "false"),
        new(IdiotProof.Core.History.HistoryOnlineWindow.EnforcedKey, SettingKind.Bool,
            "History database online window (IP-T542), used only when the history database is on Azure SQL Database (serverless): on trading days the Hub wakes it at the start of the window and keeps it awake to the end; outside the window the history keeper and the scheduled research scan and gapper radar leave it asleep. false = no window: it may be used at any hour and the Hub neither wakes nor holds it.", "true"),
        new(IdiotProof.Core.History.HistoryOnlineWindow.LeadMinutesKey, SettingKind.Int,
            "History database online window: minutes before the 04:00 ET premarket open at which the window starts and the Hub wakes the database (30 = 03:30 ET).", "30", Min: 0m, Max: IdiotProof.Core.History.HistoryOnlineWindow.MaxLeadOrTailMinutes),
        new(IdiotProof.Core.History.HistoryOnlineWindow.TailMinutesKey, SettingKind.Int,
            "History database online window: minutes after the after-hours close (20:00 ET, 17:00 on an early-close day) at which the window ends (30 = 20:30 ET).", "30", Min: 0m, Max: IdiotProof.Core.History.HistoryOnlineWindow.MaxLeadOrTailMinutes),
        new(IdiotProof.Core.History.HistoryOnlineWindow.KeepAliveMinutesKey, SettingKind.Int,
            "History database online window: minutes between the Hub's keep-alive queries inside the window. Keep it well under the database's auto-pause delay (60 minutes).", "20", Min: 1m, Max: IdiotProof.Core.History.HistoryOnlineWindow.MaxKeepAliveMinutes),
        new(IdiotProof.Core.History.HistoryOnlineWindow.WakeRetrySecondsKey, SettingKind.Int,
            "History database online window: seconds between wake attempts after one failed (each attempt already waits out a resume, about 90 seconds).", "60", Min: 5m, Max: 3600m),
        new(IdiotProof.Core.History.HistoryOnlineWindow.NightlyAfterCloseMinutesKey, SettingKind.Int,
            "Permanent market history on Azure SQL Database only: minutes after the after-hours close at which the nightly top-up and splits/filings pass starts (5 = 20:05 ET), kept inside the online window's tail. On a local SQL Server the pass stays at 20:15 ET.", "5", Min: 0m, Max: IdiotProof.Core.History.HistoryOnlineWindow.MaxLeadOrTailMinutes),
        new(CanonRepairPolicy.EnabledKey, SettingKind.Bool,
            "Quarantine auto-repair (IP-T525): the Monitor repairs a quarantined Paper or Sandbox strategy's canon from its own script when nothing is lost (flat strategies only, audited as strategy-repaired). false = never repair automatically.", "true"),
        new(CanonRepairPolicy.LiveKey, SettingKind.Bool,
            "Quarantine auto-repair also covers Live strategies. Off by default: a Live strategy's canon does not change without the owner seeing it.", "false"),
        new(GenerationRepairPolicy.RoundsKey, SettingKind.Int,
            "Generator repair loop (IP-T524): how many extra times an LLM-written strategy (strategy from-text, gapper from-transcript) is sent back to the model with its validation errors and proposed fixes before the operation fails. 0 = one call, no repair round. Fixes the validator can make by itself cost no call and are not counted.",
            GenerationRepairPolicy.DefaultRounds.ToString(CultureInfo.InvariantCulture), Min: 0m, Max: GenerationRepairPolicy.MaxAllowedRounds),
        new(IdiotProof.Core.Catalysts.CatalystIntakeSettings.EnabledKey, SettingKind.Bool,
            "Catalyst intake (IP-T443): the Hub's Monitor streams Alpaca news (and EDGAR when polled) into CatalystEvents, pre-classified and scored. Leader-only; needs catalyst.intake.ownerUserId; places nothing.", "false"),
        new(IdiotProof.Core.Catalysts.CatalystIntakeSettings.OwnerKey, SettingKind.Text,
            "Catalyst intake: the user GUID whose Alpaca data keys the news stream uses."),
        new(IdiotProof.Core.Catalysts.CatalystIntakeSettings.EdgarPollingKey, SettingKind.Bool,
            "Catalyst intake: also poll SEC EDGAR's current-filings feed.", "false"),
        new(IdiotProof.Core.Catalysts.CatalystIntakeSettings.LlmClassifyKey, SettingKind.Bool,
            "Catalyst intake: queue each new event for Haiku classification (through MindAttic.Legion; spends Anthropic credit), capped by catalyst.intake.llmDailyBudget.", "false"),
        new(IdiotProof.Core.Catalysts.CatalystIntakeSettings.LlmDailyBudgetKey, SettingKind.Int,
            "Catalyst intake (IP-T447): live Haiku classification calls allowed per ET day; every attempt counts; 0 = none. The backfill has its own per-run budget.",
            IdiotProof.Core.Catalysts.CatalystIntakeSettings.DefaultLlmDailyBudget.ToString(CultureInfo.InvariantCulture), Min: 0m, Max: 100000m),
        // IP-T602: one on/off switch and one URL override per extra intake source (wires and social).
        .. IdiotProof.Core.Catalysts.CatalystSources.SettingDefinitions(),
        new(IdiotProof.Core.Monitoring.Formation.FormationSettings.TodProfilePathKey, SettingKind.Text,
            "Formation recorder (IP-T447): full path of the gapper study's time-of-day volume profile (docs/research/gapper-study-2026-11/data/tod-profile.json). Unset = VolRatioTod stays null live."),
        new(IdiotProof.Core.Monitoring.Formation.FormationSettings.TodDecileEdgesDollarsKey, SettingKind.Text,
            "Formation recorder: the study's 9 ADV20 dollar-volume decile edges (USD, comma-separated, ascending), so a live symbol lands in the same profile decile the study's universe gave it. Wins over formation.todDecileEdges."),
        new(IdiotProof.Core.Monitoring.Formation.FormationSettings.MoveStartEnabledKey, SettingKind.Bool,
            "Move-start watch (IP-T628): from 09:30 to 16:00 ET the leader records amendment 09's move start (m0) for the formation universe as Source=live-m0 shadow rows and a move-start-alert audit row. Watch only, never an order; needs formation.enabled, owner, paper, and the TOD profile + decile edges.", "false"),
        new(IdiotProof.Core.Monitoring.Formation.FormationSettings.TodDecileEdgesKey, SettingKind.Text,
            "Formation recorder: ADV20 share-volume decile edges (comma-separated, ascending); an approximation used only while formation.todDecileEdgesDollars is unset."),
        new("decay.be-bex.latest", SettingKind.Json, "BE/BEX decay scanner: its latest reading (written by the scanner).", Internal: true),
        new("decay.be-bex.disarmed.*", SettingKind.Bool, "BE/BEX decay scanner: it disarmed this strategy itself (written by the scanner).", Internal: true),
        new("ui.logAlertLastSeen", SettingKind.Timestamp, "When the log-alert badge was last cleared (written by the web app).", Internal: true),
    ];

    /// <summary>The definition covering <paramref name="key"/>, or null for an unknown key.</summary>
    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Matches(key.Trim()));
}
