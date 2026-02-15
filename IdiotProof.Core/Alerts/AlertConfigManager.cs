// ============================================================================
// AlertConfigManager - Load/Save Alert Configuration
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Logging;
using IdiotProof.Settings;

namespace IdiotProof.Alerts;

/// <summary>
/// Manages alert configuration loading and saving.
/// </summary>
public static class AlertConfigManager
{
    private static string ConfigPath => Path.Combine(SettingsManager.GetDataFolder(), "alert-config.json");
    
    /// <summary>
    /// Loads alert configuration from disk.
    /// </summary>
    public static AlertConfig Load()
    {
        var config = new AlertConfig();
        
        try
        {
            if (!File.Exists(ConfigPath))
            {
                CreateDefaultConfig();
                return config;
            }
            
            var json = File.ReadAllText(ConfigPath);
            var doc = JsonNode.Parse(json);
            if (doc == null) return config;
            
            // Discord
            var discord = doc["discord"];
            if (discord != null)
            {
                config.DiscordEnabled = discord["enabled"]?.GetValue<bool>() ?? false;
                config.DiscordWebhookUrl = discord["webhookUrl"]?.GetValue<string>();
                
                // Check for placeholder
                if (config.DiscordWebhookUrl?.Contains("YOUR_") == true)
                    config.DiscordEnabled = false;
            }
            
            // Email
            var email = doc["email"];
            if (email != null)
            {
                config.EmailEnabled = email["enabled"]?.GetValue<bool>() ?? false;
                config.SmtpServer = email["smtpServer"]?.GetValue<string>();
                config.SmtpPort = email["smtpPort"]?.GetValue<int>() ?? 587;
                config.SmtpUsername = email["username"]?.GetValue<string>();
                config.SmtpPassword = email["password"]?.GetValue<string>();
                config.EmailFrom = email["from"]?.GetValue<string>();
                config.EmailTo = email["to"]?.GetValue<string>();
            }
            
            // SMS
            var sms = doc["sms"];
            if (sms != null)
            {
                config.SmsEnabled = sms["enabled"]?.GetValue<bool>() ?? false;
                config.TwilioAccountSid = sms["twilioAccountSid"]?.GetValue<string>();
                config.TwilioAuthToken = sms["twilioAuthToken"]?.GetValue<string>();
                config.TwilioFromNumber = sms["fromNumber"]?.GetValue<string>();
                config.SmsToNumber = sms["toNumber"]?.GetValue<string>();
                
                // Check for placeholder
                if (config.TwilioAccountSid?.Contains("YOUR_") == true)
                    config.SmsEnabled = false;
            }
            
            // Telegram
            var telegram = doc["telegram"];
            if (telegram != null)
            {
                config.TelegramEnabled = telegram["enabled"]?.GetValue<bool>() ?? false;
                config.TelegramBotToken = telegram["botToken"]?.GetValue<string>();
                config.TelegramChatId = telegram["chatId"]?.GetValue<string>();
                
                // Check for placeholder
                if (config.TelegramBotToken?.Contains("YOUR_") == true)
                    config.TelegramEnabled = false;
            }
            
            // Detection thresholds
            var detection = doc["detection"];
            if (detection != null)
            {
                config.MinPercentChangeToAlert = detection["minPercentChange"]?.GetValue<double>() ?? 3.0;
                config.MinVolumeRatioToAlert = detection["minVolumeRatio"]?.GetValue<double>() ?? 2.0;
                config.MinConfidenceToAlert = detection["minConfidence"]?.GetValue<int>() ?? 60;
                config.CooldownPerSymbol = TimeSpan.FromMinutes(detection["cooldownMinutes"]?.GetValue<int>() ?? 5);
            }
        }
        catch (Exception ex)
        {
            ConsoleLog.Info($"[AlertConfig] Error loading: {ex.Message}");
        }
        
        return config;
    }
    
    /// <summary>
    /// Creates default configuration file.
    /// </summary>
    public static void CreateDefaultConfig()
    {
        var defaultJson = @"{
  ""description"": ""IdiotProof Alert Configuration"",
  
  ""discord"": {
    ""enabled"": false,
    ""webhookUrl"": ""YOUR_DISCORD_WEBHOOK_URL_HERE"",
    ""instructions"": ""Server Settings > Integrations > Webhooks > New Webhook""
  },
  
  ""email"": {
    ""enabled"": false,
    ""smtpServer"": ""smtp.gmail.com"",
    ""smtpPort"": 587,
    ""username"": ""your.email@gmail.com"",
    ""password"": ""your_app_password"",
    ""from"": ""your.email@gmail.com"",
    ""to"": ""your.email@gmail.com""
  },
  
  ""sms"": {
    ""enabled"": false,
    ""twilioAccountSid"": ""YOUR_TWILIO_ACCOUNT_SID"",
    ""twilioAuthToken"": ""YOUR_TWILIO_AUTH_TOKEN"",
    ""fromNumber"": ""+1234567890"",
    ""toNumber"": ""+1234567890""
  },
  
  ""telegram"": {
    ""enabled"": false,
    ""botToken"": ""YOUR_BOT_TOKEN"",
    ""chatId"": ""YOUR_CHAT_ID""
  },
  
  ""detection"": {
    ""minPercentChange"": 3.0,
    ""timeWindowMinutes"": 3,
    ""minVolumeRatio"": 2.0,
    ""minConfidence"": 60,
    ""cooldownMinutes"": 5
  },
  
  ""trading"": {
    ""defaultRiskDollars"": 50.0,
    ""setupExpirationMinutes"": 5
  }
}";
        
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, defaultJson);
            ConsoleLog.Info($"[AlertConfig] Created default config at: {ConfigPath}");
        }
        catch (Exception ex)
        {
            ConsoleLog.Info($"[AlertConfig] Error creating default config: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Prints current alert configuration status.
    /// </summary>
    public static void PrintStatus(AlertConfig config)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  ALERT CHANNELS                                                          ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();
        
        PrintChannel("Discord", config.DiscordEnabled, !string.IsNullOrEmpty(config.DiscordWebhookUrl) && !config.DiscordWebhookUrl.Contains("YOUR_"));
        PrintChannel("Email", config.EmailEnabled, !string.IsNullOrEmpty(config.SmtpServer));
        PrintChannel("SMS", config.SmsEnabled, !string.IsNullOrEmpty(config.TwilioAccountSid) && !config.TwilioAccountSid.Contains("YOUR_"));
        PrintChannel("Telegram", config.TelegramEnabled, !string.IsNullOrEmpty(config.TelegramBotToken) && !config.TelegramBotToken.Contains("YOUR_"));
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  DETECTION THRESHOLDS                                                    ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();
        
        Console.WriteLine($"║  Min % Change:      {config.MinPercentChangeToAlert:F1}%                                              ║");
        Console.WriteLine($"║  Min Volume Ratio:  {config.MinVolumeRatioToAlert:F1}x                                               ║");
        Console.WriteLine($"║  Min Confidence:    {config.MinConfidenceToAlert}%                                               ║");
        Console.WriteLine($"║  Cooldown:          {(int)config.CooldownPerSymbol.TotalMinutes} minutes per symbol                              ║");
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        
        Console.WriteLine();
        Console.WriteLine($"Config file: {ConfigPath}");
        Console.WriteLine();
    }
    
    private static void PrintChannel(string name, bool enabled, bool configured)
    {
        var status = enabled && configured ? "[ON] " : 
                    !configured ? "[---]" : 
                    "[OFF]";
        var color = enabled && configured ? ConsoleColor.Green : 
                   !configured ? ConsoleColor.DarkGray : 
                   ConsoleColor.Yellow;
        
        Console.Write("║  ");
        Console.ForegroundColor = color;
        Console.Write($"{status} {name,-12}");
        Console.ResetColor();
        
        if (!configured)
            Console.Write("(not configured)");
        else if (!enabled)
            Console.Write("(disabled)");
        else
            Console.Write("(active)");
        
        Console.WriteLine("".PadRight(45) + "║");
    }
}
