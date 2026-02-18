// ============================================================================
// BreakoutSetupAlerts - Sends notifications when setups trigger or confirm
// ============================================================================
//
// Integrates with the alert system to send Discord/Email/SMS/Telegram
// notifications when breakout setups change state.
// ============================================================================

using IdiotProof.Scripting;

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Sends alerts for breakout setup state changes.
/// </summary>
public sealed class BreakoutSetupAlerts
{
    private readonly ILogger<BreakoutSetupAlerts> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    
    private string? _discordWebhookUrl;
    private bool _alertsEnabled;
    
    public BreakoutSetupAlerts(
        ILogger<BreakoutSetupAlerts> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;
        
        LoadConfig();
    }
    
    private void LoadConfig()
    {
        _alertsEnabled = _config.GetValue("Alerts:Enabled", false);
        _discordWebhookUrl = _config["Alerts:Discord:WebhookUrl"];
    }
    
    /// <summary>
    /// Sends an alert for a setup state change.
    /// </summary>
    public async Task SendStateChangeAlertAsync(BreakoutSetup setup, SetupState previousState)
    {
        if (!_alertsEnabled)
            return;
        
        // Only alert on significant state changes
        if (setup.State != SetupState.Triggered && 
            setup.State != SetupState.Confirmed &&
            setup.State != SetupState.Invalidated)
            return;
        
        var message = BuildAlertMessage(setup, previousState);
        
        // Send to Discord
        if (!string.IsNullOrEmpty(_discordWebhookUrl))
        {
            await SendDiscordAlertAsync(message);
        }
        
        _logger.LogInformation("Sent setup alert: {Symbol} → {State}", setup.Symbol, setup.State);
    }
    
    private SetupAlertMessage BuildAlertMessage(BreakoutSetup setup, SetupState previousState)
    {
        var emoji = setup.State switch
        {
            SetupState.Triggered => "🚀",
            SetupState.Confirmed => "✅",
            SetupState.Invalidated => "❌",
            SetupState.Completed => "🎯",
            _ => "📊"
        };
        
        var title = setup.State switch
        {
            SetupState.Triggered => $"{emoji} TRIGGERED: {setup.Symbol}",
            SetupState.Confirmed => $"{emoji} CONFIRMED: {setup.Symbol} - Ready to Enter!",
            SetupState.Invalidated => $"{emoji} INVALIDATED: {setup.Symbol}",
            SetupState.Completed => $"{emoji} COMPLETED: {setup.Symbol}",
            _ => $"{emoji} {setup.Symbol}: {setup.State}"
        };
        
        var description = setup.State switch
        {
            SetupState.Triggered => $"Price broke above ${setup.TriggerPrice:F2}. Watching for pullback.",
            SetupState.Confirmed => $"Pullback confirmed! Entry ready near ${setup.CurrentPrice:F2}",
            SetupState.Invalidated => $"Setup failed. Price dropped below ${setup.InvalidationPrice:F2}",
            SetupState.Completed => $"All targets hit! Great trade.",
            _ => $"State changed from {previousState} to {setup.State}"
        };
        
        return new SetupAlertMessage
        {
            Title = title,
            Description = description,
            Setup = setup,
            Timestamp = DateTime.UtcNow
        };
    }
    
    private async Task SendDiscordAlertAsync(SetupAlertMessage message)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            
            var color = message.Setup.State switch
            {
                SetupState.Triggered => 0xFFA500, // Orange
                SetupState.Confirmed => 0x00FF00, // Green
                SetupState.Invalidated => 0xFF0000, // Red
                SetupState.Completed => 0x00FFFF, // Cyan
                _ => 0x808080 // Gray
            };
            
            var targets = string.Join("\n", message.Setup.Targets.Select(t =>
            {
                var status = t.IsHit ? "✓" : "○";
                var pct = (t.Price - message.Setup.TriggerPrice) / message.Setup.TriggerPrice * 100;
                return $"{status} {t.Label}: ${t.Price:F2} ({pct:+0.0}%)";
            }));
            
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = message.Title,
                        description = message.Description,
                        color = color,
                        fields = new object[]
                        {
                            new { name = "Pattern", value = message.Setup.Pattern, inline = true },
                            new { name = "Confidence", value = $"{message.Setup.ConfidenceScore}%", inline = true },
                            new { name = "Bias", value = message.Setup.Bias, inline = true },
                            new { name = "Trigger", value = $"${message.Setup.TriggerPrice:F2}", inline = true },
                            new { name = "Support", value = $"${message.Setup.SupportPrice:F2}", inline = true },
                            new { name = "Stop", value = $"${message.Setup.InvalidationPrice:F2}", inline = true },
                            new { name = "Targets", value = targets, inline = false },
                            new { name = "R:R", value = $"{message.Setup.RiskRewardRatio:F1}:1", inline = true },
                            new { name = "Gap", value = $"{message.Setup.GapPercent:+0.0;-0.0}%", inline = true },
                            new { name = "Volume", value = $"{message.Setup.VolumeRatio:F1}x", inline = true }
                        },
                        footer = new
                        {
                            text = "NO BREAK, NO TRADE | IdiotProof"
                        },
                        timestamp = message.Timestamp.ToString("o")
                    }
                }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(_discordWebhookUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discord webhook failed: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord alert");
        }
    }
    
    /// <summary>
    /// Sends a summary of confirmed setups (e.g., at market open).
    /// </summary>
    public async Task SendConfirmedSetupsSummaryAsync(IReadOnlyList<BreakoutSetup> confirmedSetups)
    {
        if (!_alertsEnabled || string.IsNullOrEmpty(_discordWebhookUrl))
            return;
        
        if (confirmedSetups.Count == 0)
            return;
        
        try
        {
            var client = _httpClientFactory.CreateClient();
            
            var setupList = string.Join("\n", confirmedSetups.Select(s =>
                $"• **{s.Symbol}** - Entry near ${s.TriggerPrice:F2} (Conf: {s.ConfidenceScore}%)"));
            
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"✅ {confirmedSetups.Count} Setups Ready for Entry",
                        description = setupList,
                        color = 0x00FF00,
                        footer = new
                        {
                            text = "Click to view details | IdiotProof"
                        },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            await client.PostAsync(_discordWebhookUrl, content);
            
            _logger.LogInformation("Sent summary alert for {Count} confirmed setups", confirmedSetups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send summary alert");
        }
    }
}

/// <summary>
/// Alert message for a setup state change.
/// </summary>
public sealed class SetupAlertMessage
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public BreakoutSetup Setup { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
