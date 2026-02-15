// ============================================================================
// AlertService - Multi-Channel Alerting System
// ============================================================================
//
// PURPOSE:
// Detect sudden market moves, pre-generate trade plans, and alert the user
// via multiple channels (Discord, Email, SMS, etc.) with one-click execution.
//
// THE PROBLEM THIS SOLVES:
// "By the time I notice a move, analyze it, calculate R:R, and fill in the
// order form, I'm just chasing the stock."
//
// THE SOLUTION:
// 1. System detects the move INSTANTLY
// 2. Pre-calculates LONG and SHORT setups with SL/TP/R:R
// 3. Sends alert with ONE-CLICK execution buttons
// 4. You just click "GO" - no scrambling
//
// ============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using IdiotProof.Settings;

namespace IdiotProof.Alerts;

/// <summary>
/// Pre-calculated trade setup ready for instant execution.
/// </summary>
public sealed class TradeSetup
{
    public string Symbol { get; init; } = "";
    public bool IsLong { get; init; }
    public double EntryPrice { get; init; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }
    public double TrailingStopPercent { get; init; }
    public int Quantity { get; init; }
    public double RiskDollars { get; init; }
    public double RewardDollars { get; init; }
    public double RiskRewardRatio { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public string SetupId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    
    /// <summary>
    /// Time until this setup expires (setups are only valid briefly).
    /// </summary>
    public TimeSpan ExpiresIn => TimeSpan.FromMinutes(5) - (DateTime.Now - GeneratedAt);
    public bool IsExpired => ExpiresIn <= TimeSpan.Zero;
    
    public string Direction => IsLong ? "LONG" : "SHORT";
    public string DirectionEmoji => IsLong ? "📈" : "📉";
    
    public string ToDiscordEmbed()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{DirectionEmoji} **{Direction} Setup** (ID: `{SetupId}`)");
        sb.AppendLine($"```");
        sb.AppendLine($"Entry:   ${EntryPrice:F2}");
        sb.AppendLine($"Stop:    ${StopLoss:F2} ({(IsLong ? "-" : "+")}{Math.Abs((StopLoss - EntryPrice) / EntryPrice * 100):F1}%)");
        sb.AppendLine($"Target:  ${TakeProfit:F2} ({(IsLong ? "+" : "-")}{Math.Abs((TakeProfit - EntryPrice) / EntryPrice * 100):F1}%)");
        sb.AppendLine($"Trail:   {TrailingStopPercent:F1}%");
        sb.AppendLine($"Qty:     {Quantity} shares");
        sb.AppendLine($"Risk:    ${RiskDollars:F2}");
        sb.AppendLine($"Reward:  ${RewardDollars:F2}");
        sb.AppendLine($"R:R:     {RiskRewardRatio:F1}:1");
        sb.AppendLine($"```");
        return sb.ToString();
    }
}

/// <summary>
/// Alert containing detected move and pre-generated trade setups.
/// </summary>
public sealed class TradingAlert
{
    public string AlertId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Symbol { get; init; } = "";
    public AlertType Type { get; init; }
    public AlertSeverity Severity { get; init; }
    
    // Move details
    public double CurrentPrice { get; init; }
    public double PreviousPrice { get; init; }
    public double PercentChange { get; init; }
    public double VolumeRatio { get; init; }  // vs average
    public TimeSpan TimeFrame { get; init; }  // How quickly the move happened
    
    // Pre-generated setups
    public TradeSetup? LongSetup { get; init; }
    public TradeSetup? ShortSetup { get; init; }
    
    // Context
    public string? NewsHeadline { get; init; }
    public string? Reason { get; init; }
    public int Confidence { get; init; }  // 0-100
    
    public string SeverityEmoji => Severity switch
    {
        AlertSeverity.Critical => "🚨",
        AlertSeverity.High => "⚠️",
        AlertSeverity.Medium => "📊",
        _ => "ℹ️"
    };
    
    public string TypeDescription => Type switch
    {
        AlertType.SuddenSpike => "SUDDEN SPIKE",
        AlertType.SuddenDrop => "SUDDEN DROP",
        AlertType.VolumeSpike => "VOLUME SPIKE",
        AlertType.Breakout => "BREAKOUT",
        AlertType.Breakdown => "BREAKDOWN",
        AlertType.GapUp => "GAP UP",
        AlertType.GapDown => "GAP DOWN",
        AlertType.NewsEvent => "NEWS EVENT",
        _ => "ALERT"
    };
    
    public string ToDiscordMessage()
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine($"{SeverityEmoji} **{Symbol} {TypeDescription}** {SeverityEmoji}");
        sb.AppendLine();
        
        // Move details
        var changeSign = PercentChange >= 0 ? "+" : "";
        var changeColor = PercentChange >= 0 ? "green" : "red";
        sb.AppendLine($"**Price:** ${CurrentPrice:F2} ({changeSign}{PercentChange:F1}% in {(int)TimeFrame.TotalMinutes}min)");
        sb.AppendLine($"**Volume:** {VolumeRatio:F1}x average");
        sb.AppendLine($"**Confidence:** {Confidence}%");
        
        if (!string.IsNullOrEmpty(NewsHeadline))
        {
            sb.AppendLine();
            sb.AppendLine($"📰 **News:** {NewsHeadline}");
        }
        
        if (!string.IsNullOrEmpty(Reason))
        {
            sb.AppendLine($"💡 **Analysis:** {Reason}");
        }
        
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        
        // Long setup
        if (LongSetup != null)
        {
            sb.AppendLine();
            sb.AppendLine(LongSetup.ToDiscordEmbed());
        }
        
        // Short setup
        if (ShortSetup != null)
        {
            sb.AppendLine();
            sb.AppendLine(ShortSetup.ToDiscordEmbed());
        }
        
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"⏱️ Setups valid for 5 minutes | Alert ID: `{AlertId}`");
        
        return sb.ToString();
    }
    
    public string ToEmailHtml()
    {
        var changeSign = PercentChange >= 0 ? "+" : "";
        var changeColor = PercentChange >= 0 ? "#00ff00" : "#ff0000";
        
        var sb = new StringBuilder();
        sb.AppendLine($"<html><body style='font-family: Arial, sans-serif; background: #1a1a1a; color: #fff; padding: 20px;'>");
        
        // Header
        sb.AppendLine($"<h1 style='color: {changeColor};'>{SeverityEmoji} {Symbol} {TypeDescription}</h1>");
        
        // Details
        sb.AppendLine($"<p><strong>Price:</strong> ${CurrentPrice:F2} <span style='color:{changeColor};'>({changeSign}{PercentChange:F1}%)</span></p>");
        sb.AppendLine($"<p><strong>Volume:</strong> {VolumeRatio:F1}x average</p>");
        sb.AppendLine($"<p><strong>Confidence:</strong> {Confidence}%</p>");
        
        if (!string.IsNullOrEmpty(NewsHeadline))
            sb.AppendLine($"<p><strong>News:</strong> {NewsHeadline}</p>");
        
        // Setups
        if (LongSetup != null)
        {
            sb.AppendLine($"<div style='background: #0a3d0a; padding: 15px; margin: 10px 0; border-radius: 5px;'>");
            sb.AppendLine($"<h3>📈 LONG Setup (ID: {LongSetup.SetupId})</h3>");
            sb.AppendLine($"<p>Entry: ${LongSetup.EntryPrice:F2} | Stop: ${LongSetup.StopLoss:F2} | Target: ${LongSetup.TakeProfit:F2}</p>");
            sb.AppendLine($"<p>Risk: ${LongSetup.RiskDollars:F2} | Reward: ${LongSetup.RewardDollars:F2} | R:R: {LongSetup.RiskRewardRatio:F1}:1</p>");
            sb.AppendLine($"</div>");
        }
        
        if (ShortSetup != null)
        {
            sb.AppendLine($"<div style='background: #3d0a0a; padding: 15px; margin: 10px 0; border-radius: 5px;'>");
            sb.AppendLine($"<h3>📉 SHORT Setup (ID: {ShortSetup.SetupId})</h3>");
            sb.AppendLine($"<p>Entry: ${ShortSetup.EntryPrice:F2} | Stop: ${ShortSetup.StopLoss:F2} | Target: ${ShortSetup.TakeProfit:F2}</p>");
            sb.AppendLine($"<p>Risk: ${ShortSetup.RiskDollars:F2} | Reward: ${ShortSetup.RewardDollars:F2} | R:R: {ShortSetup.RiskRewardRatio:F1}:1</p>");
            sb.AppendLine($"</div>");
        }
        
        sb.AppendLine($"<p style='color: #888; font-size: 12px;'>Setups valid for 5 minutes | Generated: {Timestamp:HH:mm:ss}</p>");
        sb.AppendLine("</body></html>");
        
        return sb.ToString();
    }
}

public enum AlertType
{
    SuddenSpike,    // Price spiked up quickly
    SuddenDrop,     // Price dropped quickly
    VolumeSpike,    // Unusual volume
    Breakout,       // Broke above resistance
    Breakdown,      // Broke below support
    GapUp,          // Gapped up from previous close
    GapDown,        // Gapped down from previous close
    NewsEvent       // Triggered by news
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Configuration for alert channels.
/// </summary>
public sealed class AlertConfig
{
    // Discord
    public bool DiscordEnabled { get; set; } = true;
    public string? DiscordWebhookUrl { get; set; }
    
    // Email
    public bool EmailEnabled { get; set; } = false;
    public string? SmtpServer { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? EmailFrom { get; set; }
    public string? EmailTo { get; set; }
    
    // Twilio SMS
    public bool SmsEnabled { get; set; } = false;
    public string? TwilioAccountSid { get; set; }
    public string? TwilioAuthToken { get; set; }
    public string? TwilioFromNumber { get; set; }
    public string? SmsToNumber { get; set; }
    
    // Telegram
    public bool TelegramEnabled { get; set; } = false;
    public string? TelegramBotToken { get; set; }
    public string? TelegramChatId { get; set; }
    
    // Thresholds
    public double MinPercentChangeToAlert { get; set; } = 3.0;
    public double MinVolumeRatioToAlert { get; set; } = 2.0;
    public int MinConfidenceToAlert { get; set; } = 60;
    public TimeSpan CooldownPerSymbol { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Multi-channel alert service.
/// </summary>
public sealed class AlertService : IDisposable
{
    private readonly AlertConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, DateTime> _lastAlertTime = new();
    private readonly object _lock = new();
    
    // Store pending setups for one-click execution
    private readonly Dictionary<string, TradeSetup> _pendingSetups = new();
    
    public AlertService(AlertConfig config)
    {
        _config = config;
        _httpClient = new HttpClient();
    }
    
    /// <summary>
    /// Event fired when an alert is generated (for console output).
    /// </summary>
    public event Action<TradingAlert>? OnAlert;
    
    /// <summary>
    /// Sends an alert through all enabled channels.
    /// </summary>
    public async Task SendAlertAsync(TradingAlert alert)
    {
        // Check cooldown
        lock (_lock)
        {
            if (_lastAlertTime.TryGetValue(alert.Symbol, out var lastTime))
            {
                if (DateTime.Now - lastTime < _config.CooldownPerSymbol)
                    return;  // Still in cooldown
            }
            _lastAlertTime[alert.Symbol] = DateTime.Now;
        }
        
        // Store setups for retrieval
        if (alert.LongSetup != null)
            _pendingSetups[alert.LongSetup.SetupId] = alert.LongSetup;
        if (alert.ShortSetup != null)
            _pendingSetups[alert.ShortSetup.SetupId] = alert.ShortSetup;
        
        // Fire local event
        OnAlert?.Invoke(alert);
        
        // Send to all enabled channels
        var tasks = new List<Task>();
        
        if (_config.DiscordEnabled && !string.IsNullOrEmpty(_config.DiscordWebhookUrl))
            tasks.Add(SendDiscordAlertAsync(alert));
        
        if (_config.EmailEnabled && !string.IsNullOrEmpty(_config.SmtpServer))
            tasks.Add(SendEmailAlertAsync(alert));
        
        if (_config.SmsEnabled && !string.IsNullOrEmpty(_config.TwilioAccountSid))
            tasks.Add(SendSmsAlertAsync(alert));
        
        if (_config.TelegramEnabled && !string.IsNullOrEmpty(_config.TelegramBotToken))
            tasks.Add(SendTelegramAlertAsync(alert));
        
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Gets a pending setup by ID for execution.
    /// </summary>
    public TradeSetup? GetSetup(string setupId)
    {
        if (_pendingSetups.TryGetValue(setupId, out var setup))
        {
            if (!setup.IsExpired)
                return setup;
            
            _pendingSetups.Remove(setupId);
        }
        return null;
    }
    
    /// <summary>
    /// Removes expired setups.
    /// </summary>
    public void CleanupExpiredSetups()
    {
        var expired = _pendingSetups.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
            _pendingSetups.Remove(key);
    }
    
    // ========================================
    // DISCORD
    // ========================================
    
    private async Task SendDiscordAlertAsync(TradingAlert alert)
    {
        try
        {
            var payload = new
            {
                content = alert.ToDiscordMessage(),
                username = "IdiotProof Trading",
                avatar_url = "https://i.imgur.com/4M34hi2.png"  // Trading chart icon
            };
            
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(_config.DiscordWebhookUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Alert] Discord failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Alert] Discord error: {ex.Message}");
        }
    }
    
    // ========================================
    // EMAIL
    // ========================================
    
    private async Task SendEmailAlertAsync(TradingAlert alert)
    {
        try
        {
            using var client = new SmtpClient(_config.SmtpServer, _config.SmtpPort)
            {
                Credentials = new NetworkCredential(_config.SmtpUsername, _config.SmtpPassword),
                EnableSsl = true
            };
            
            var message = new MailMessage
            {
                From = new MailAddress(_config.EmailFrom!),
                Subject = $"🚨 {alert.Symbol} {alert.TypeDescription} ({alert.PercentChange:+0.0;-0.0}%)",
                Body = alert.ToEmailHtml(),
                IsBodyHtml = true
            };
            message.To.Add(_config.EmailTo!);
            
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Alert] Email error: {ex.Message}");
        }
    }
    
    // ========================================
    // SMS (Twilio)
    // ========================================
    
    private async Task SendSmsAlertAsync(TradingAlert alert)
    {
        try
        {
            var changeSign = alert.PercentChange >= 0 ? "+" : "";
            var message = $"{alert.Symbol} {alert.TypeDescription}\n" +
                         $"${alert.CurrentPrice:F2} ({changeSign}{alert.PercentChange:F1}%)\n" +
                         $"LONG: Entry ${alert.LongSetup?.EntryPrice:F2} SL ${alert.LongSetup?.StopLoss:F2}\n" +
                         $"SHORT: Entry ${alert.ShortSetup?.EntryPrice:F2} SL ${alert.ShortSetup?.StopLoss:F2}";
            
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{_config.TwilioAccountSid}/Messages.json";
            
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = _config.SmsToNumber!,
                ["From"] = _config.TwilioFromNumber!,
                ["Body"] = message
            });
            
            var authToken = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_config.TwilioAccountSid}:{_config.TwilioAuthToken}"));
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
            
            await _httpClient.PostAsync(url, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Alert] SMS error: {ex.Message}");
        }
    }
    
    // ========================================
    // TELEGRAM
    // ========================================
    
    private async Task SendTelegramAlertAsync(TradingAlert alert)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_config.TelegramBotToken}/sendMessage";
            
            var payload = new
            {
                chat_id = _config.TelegramChatId,
                text = alert.ToDiscordMessage(),  // Markdown works in Telegram too
                parse_mode = "Markdown"
            };
            
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            await _httpClient.PostAsync(url, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Alert] Telegram error: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
