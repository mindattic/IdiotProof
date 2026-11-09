using Microsoft.Extensions.DependencyInjection.Extensions;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Core.Hosting;

/// <summary>
/// The unattended evaluator + executor (IP-LAW-10): the scanners the tick loop drives and the
/// <see cref="MonitorWorker"/> hosted service. Whichever host calls this runs THE Monitor for
/// its database — the SQL leader lease keeps a second copy from trading. Requires
/// <see cref="CoreServiceRegistration.AddIdiotProofCore"/> and
/// <see cref="TradingServiceRegistration.AddIdiotProofTrading"/>.
/// </summary>
public static class MonitorServiceRegistration
{
    public static IServiceCollection AddIdiotProofMonitor(this IServiceCollection services)
    {
        // On-demand gapper generator (the auto-gapper operator command; the 3:55 ET auto-arm is
        // owner-gated and paper-only).
        services.AddSingleton<AutoGapperScanner>();

        // Premarket blow-off/fade detector — driven by the tick loop (Mon-Fri, 9:00-10:00 AM ET,
        // every 5 min); detection-only, never creates a strategy or places an order.
        services.AddSingleton<EmailSmsAlertSender>();
        services.AddSingleton<PremarketFadeScanner>();

        // BE/BEX decay-signal scanner — driven by the tick loop (Mon-Fri, once 16:30-17:00 ET);
        // arms/disarms the BE pullback strategy via StrategyRepository, never orders directly.
        services.AddSingleton<BeBexDecayScanner>();

        // What the loop is doing (monitor status|heartbeat|lease, IP-T256) and the SettingsKv
        // pause switch it honors at the top of every tick (IP-T80).
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MonitorRuntimeState>(sp => new MonitorRuntimeState(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<MonitorPauseFlag>(sp => new MonitorPauseFlag(
            sp.GetRequiredService<IdiotProof.Core.Services.SettingsRepository>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IMonitorLeaseProbe, SqlMonitorLeaseProbe>();

        services.AddHostedService<MonitorWorker>();

        // Texts the owner (SMTP, not SQL) when the Monitor stops evaluating (IP-T359).
        services.AddHostedService<MonitorOutageWatchdog>();

        // Living strategies (IP-T373): idle unless SettingsKv adaptation.enabled = "true"; paper-only, per-strategy opt-in.
        services.AddSingleton<IdiotProof.Core.Adaptation.AdaptationService>();
        services.AddHostedService<IdiotProof.Core.Adaptation.AdaptationWorker>();

        // Intraday exhaustion templates (IP-US-S2): idle unless SettingsKv exhaustion.enabled = "true"; paper-only, leader-only.
        services.AddSingleton<ExhaustionScanner>();
        services.AddHostedService<ExhaustionScanWorker>();

        // Catalyst intake (IP-T443): idle unless SettingsKv catalyst.intake.enabled = "true"; leader-only; research rows only.
        services.TryAddSingleton<IdiotProof.DataFeeds.Streaming.IStreamSocketFactory, IdiotProof.DataFeeds.Streaming.ClientWebSocketFactory>();
        services.AddHostedService<IdiotProof.Core.Catalysts.CatalystIntakeWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<IdiotProof.Core.Catalysts.CatalystClassifier>());

        // Formation recorder (IP-T443): idle unless SettingsKv formation.enabled = "true"; leader-only, paper-only,
        // 03:55-09:45 ET weekdays; research rows through IGapperSnapshotSink only, never an arm. Also runs the
        // scheduled `gapper auto` pass when gapper.auto.scheduledEt is set.
        services.AddSingleton<IdiotProof.Core.Monitoring.Formation.FormationUniverseBuilder>();
        services.AddSingleton<IdiotProof.Core.Monitoring.Formation.FormationBarCache>();
        services.AddSingleton<IdiotProof.Core.Monitoring.Formation.IFormationBarSource>(sp => sp.GetRequiredService<IdiotProof.Core.Monitoring.Formation.FormationBarCache>());
        services.AddScoped<IdiotProof.Core.Monitoring.Formation.GapperFormationScanner>();
        services.AddHostedService<IdiotProof.Core.Monitoring.Formation.GapperFormationWorker>();

        // Fade-short borrow pilot (IP-T480): idle unless SettingsKv fadeShort.pilot.enabled = "true"; leader-only,
        // paper-only, 09:40 and 09:46 ET weekdays. NO ORDERS: Alpaca is reached only through the GET-only
        // IFadeShortBorrowReader (asset, advisory locate quote, bars, movers); rows go to FadeShortBorrowObservations.
        services.AddSingleton<IdiotProof.Core.Monitoring.FadeShort.FadeShortPilotSettings>();
        services.AddSingleton<IdiotProof.Core.Monitoring.FadeShort.IFadeShortBorrowReader, IdiotProof.Core.Monitoring.FadeShort.AlpacaFadeShortBorrowReader>();
        services.AddHostedService<IdiotProof.Core.Monitoring.FadeShort.FadeShortBorrowPilotWorker>();

        // Permanent market history (IP-T454): idle unless SettingsKv history.backfill.enabled = "true"; leader-only;
        // queues 5 years for every newly met ticker and the last session nightly; writes the history database only.
        services.AddHostedService<IdiotProof.Core.History.HistoryBackfillWorker>();
        return services;
    }
}
