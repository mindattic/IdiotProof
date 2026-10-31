using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;

namespace IdiotProof.Engine.Settings;

/// <summary>
/// IdiotProof's app-level settings in MindAttic Vault (<c>%APPDATA%\MindAttic\IdiotProof\settings.json</c>), edited in
/// MindAttic.Vault.Desktop. Overlaid on <see cref="AppSettings"/> at every load; imported once from today's
/// <c>app-settings.json</c>. Each trader's own preferences, limits and keys stay in IdiotProof; the app-level Alpaca keys and
/// the admin password hash stay in <c>app-settings.json</c> and Vault's credential stores, never here.
/// </summary>
[SettingsApp(AppId, "IdiotProof", Icon = "chart", Description = "Strategy builder and live trading monitor.")]
[UsesCredential("claude", Purpose = "Strategy review and LLM voting")]
[UsesCredential("openai", Purpose = "LLM voting")]
[UsesCredential("gemini", Purpose = "LLM voting")]
[UsesCredential("deepseek", Purpose = "LLM voting")]
[UsesCredential("mistral", Purpose = "LLM voting")]
[UsesCredential("alpaca", Purpose = "Market data and paper/live trading")]
public sealed class IdiotProofSettings
{
    public const string AppId = "IdiotProof";

    [Setting("Alpaca paper trading", Group = "Broker", Order = 1, Help = "On: the app-level Alpaca account trades on paper. Off: live money.")]
    public bool AlpacaIsPaper { get; set; } = true;

    [Setting("Default broker", Group = "Broker", Order = 2, Help = "Where orders go unless a trader picks their own.")]
    [SettingChoices("Sandbox", "alpaca")]
    public string DefaultBroker { get; set; } = "Sandbox";

    [Setting("Default data feed", Group = "Broker", Order = 3, Help = "Market data source, e.g. Alpaca.")]
    public string DefaultDataFeed { get; set; } = "Alpaca";

    [Setting("Time zone", Group = "General", Order = 1, Help = "Windows time zone id used for market hours, e.g. Central Standard Time.")]
    public string Timezone { get; set; } = "Central Standard Time";

    [Setting("Show connection messages", Group = "General", Order = 2)]
    public bool ShowConnectionMessages { get; set; } = true;

    [Setting("LLM voting", Group = "AI voting", Order = 1, Help = "Have a panel of LLMs vote on strategy decisions.")]
    public bool LlmVotingEnabled { get; set; }

    [Setting("Consensus threshold", Group = "AI voting", Order = 2, Help = "Share of voters that must agree (0.66 = 66%).")]
    [SettingRange(0, 1)]
    public decimal LlmConsensusThreshold { get; set; } = 0.66m;

    [Setting("Evaluation interval (s)", Group = "AI voting", Order = 3)]
    [SettingRange(1, 3600)]
    public int StrategyEvaluationIntervalSeconds { get; set; } = 30;

    [Setting("Concurrent evaluations", Group = "AI voting", Order = 4)]
    [SettingRange(1, 64)]
    public int MaxConcurrentEvaluations { get; set; } = 4;

    [Setting("Voter model", Group = "AI voting", Order = 5)]
    public string LlmVoterModel { get; set; } = "claude-sonnet-5";

    [Setting("Minimum gap (%)", Group = "Auto-gapper", Order = 1, Help = "On-demand gap scan (operator CLI auto-gapper).")]
    public double AutoGapperMinGapPercent { get; set; } = 15;

    [Setting("Minimum price", Group = "Auto-gapper", Order = 2)]
    public double AutoGapperMinPrice { get; set; } = 1;

    [Setting("Maximum symbols", Group = "Auto-gapper", Order = 3)]
    [SettingRange(1, 100)]
    public int AutoGapperMaxCount { get; set; } = 5;

    [Setting("Broker mode", Group = "Auto-gapper", Order = 4)]
    [SettingChoices("paper", "live")]
    public string AutoGapperBrokerMode { get; set; } = "paper";

    public static IdiotProofSettings From(AppSettings s) => new()
    {
        AlpacaIsPaper = s.AlpacaIsPaper, DefaultBroker = s.DefaultBroker, DefaultDataFeed = s.DefaultDataFeed,
        Timezone = s.Timezone, ShowConnectionMessages = s.ShowConnectionMessages, LlmVotingEnabled = s.LlmVotingEnabled,
        LlmConsensusThreshold = s.LlmConsensusThreshold, StrategyEvaluationIntervalSeconds = s.StrategyEvaluationIntervalSeconds,
        MaxConcurrentEvaluations = s.MaxConcurrentEvaluations, LlmVoterModel = s.LlmVoterModel,
        AutoGapperMinGapPercent = s.AutoGapperMinGapPercent, AutoGapperMinPrice = s.AutoGapperMinPrice,
        AutoGapperMaxCount = s.AutoGapperMaxCount, AutoGapperBrokerMode = s.AutoGapperBrokerMode,
    };

    public void ApplyTo(AppSettings s)
    {
        s.AlpacaIsPaper = AlpacaIsPaper; s.DefaultBroker = DefaultBroker; s.DefaultDataFeed = DefaultDataFeed;
        s.Timezone = Timezone; s.ShowConnectionMessages = ShowConnectionMessages; s.LlmVotingEnabled = LlmVotingEnabled;
        s.LlmConsensusThreshold = LlmConsensusThreshold; s.StrategyEvaluationIntervalSeconds = StrategyEvaluationIntervalSeconds;
        s.MaxConcurrentEvaluations = MaxConcurrentEvaluations; s.LlmVoterModel = LlmVoterModel;
        s.AutoGapperMinGapPercent = AutoGapperMinGapPercent; s.AutoGapperMinPrice = AutoGapperMinPrice;
        s.AutoGapperMaxCount = AutoGapperMaxCount; s.AutoGapperBrokerMode = AutoGapperBrokerMode;
    }

    /// <summary>The Vault store; tests pass a temp one.</summary>
    public static Func<JsonSettingsStore<IdiotProofSettings>> StoreFactory { get; set; } = JsonSettingsStore<IdiotProofSettings>.ForSettingsApp;
}
