using IdiotProof.Monitor;

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

        services.AddHostedService<MonitorWorker>();
        return services;
    }
}
