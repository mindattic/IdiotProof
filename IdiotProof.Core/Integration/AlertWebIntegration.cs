// ============================================================================
// Alert to Web Integration - Sends Alerts to Web Frontend
// ============================================================================
// This helper bridges the SuddenMoveDetector alerts to the Web frontend.
// When an alert fires in Core, it gets pushed to Web for display.
// ============================================================================

using IdiotProof.Alerts;
using IdiotProof.Services;

namespace IdiotProof.Integration;

/// <summary>
/// Integrates alert system with web frontend.
/// </summary>
public sealed class AlertWebIntegration : IDisposable
{
    private readonly SuddenMoveDetector _detector;
    private readonly AlertService _alertService;
    private readonly WebFrontendClient _webClient;
    private bool _disposed;
    
    public AlertWebIntegration(
        SuddenMoveDetector detector,
        AlertService alertService,
        WebFrontendClient webClient)
    {
        _detector = detector;
        _alertService = alertService;
        _webClient = webClient;
        
        // Wire up event handlers
        _detector.OnSuddenMoveDetected += OnSuddenMove;
        _alertService.OnAlert += OnAlertGenerated;
    }
    
    private async void OnSuddenMove(TradingAlert alert)
    {
        // Send alert through all channels (Discord, Email, etc.)
        await _alertService.SendAlertAsync(alert);
    }
    
    private async void OnAlertGenerated(TradingAlert alert)
    {
        // Also push to web frontend for live display
        try
        {
            await _webClient.SendAlertAsync(
                alert.Symbol,
                alert.Type.ToString(),
                alert.Severity.ToString(),
                alert.CurrentPrice,
                alert.PercentChange,
                alert.Confidence,
                alert.Reason ?? "",
                alert.LongSetup,
                alert.ShortSetup);
            
            Console.WriteLine($"[AlertWeb] Pushed alert to web: {alert.Symbol} {alert.TypeDescription}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AlertWeb] Failed to push to web: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _detector.OnSuddenMoveDetected -= OnSuddenMove;
        _alertService.OnAlert -= OnAlertGenerated;
    }
}

/// <summary>
/// Extension methods for easy setup.
/// </summary>
public static class AlertWebIntegrationExtensions
{
    /// <summary>
    /// Creates and wires up the alert web integration.
    /// </summary>
    public static AlertWebIntegration CreateAlertWebIntegration(
        this SuddenMoveDetector detector,
        AlertService alertService,
        WebFrontendClient webClient)
    {
        return new AlertWebIntegration(detector, alertService, webClient);
    }
}
