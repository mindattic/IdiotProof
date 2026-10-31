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
        return services;
    }
}
