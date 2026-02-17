// ============================================================================
// Settings Provider - Builds UI metadata from AppSettings
// ============================================================================

using IdiotProof.Shared.Settings;
using IdiotProof.Settings;
using IdiotProof.Enums;

namespace IdiotProof.Web.Services.Settings;

/// <summary>
/// Provides settings metadata and manages runtime settings updates.
/// </summary>
public class SettingsProvider
{
    private SettingsBundle? _cachedBundle;
    
    /// <summary>
    /// Gets all application settings with their metadata for UI rendering.
    /// </summary>
    public SettingsBundle GetAllSettings()
    {
        if (_cachedBundle != null)
            return _cachedBundle;
            
        var settings = new List<SettingDefinition>
        {
            // ===== IB Connection Settings =====
            new()
            {
                Key = "Host",
                DisplayName = "IB Host",
                Category = "IB Connection",
                Description = "IP address or hostname of TWS/Gateway",
                ControlType = SettingControlType.IpAddress,
                DataType = SettingDataType.String,
                Value = AppSettings.Host,
                DefaultValue = "127.0.0.1",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 1
            },
            new()
            {
                Key = "Port",
                DisplayName = "IB Port",
                Category = "IB Connection",
                Description = "TWS Paper: 7497, TWS Live: 7496, Gateway Paper: 4002, Gateway Live: 4001",
                ControlType = SettingControlType.Select,
                DataType = SettingDataType.Int,
                Value = AppSettings.Port,
                DefaultValue = 4002,
                Options =
                [
                    new() { Value = "7497", Label = "TWS Paper (7497)" },
                    new() { Value = "7496", Label = "TWS Live (7496)" },
                    new() { Value = "4002", Label = "Gateway Paper (4002)" },
                    new() { Value = "4001", Label = "Gateway Live (4001)" }
                ],
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 2
            },
            new()
            {
                Key = "ClientId",
                DisplayName = "Client ID",
                Category = "IB Connection",
                Description = "Unique client ID for this connection (must be unique per TWS instance)",
                ControlType = SettingControlType.Number,
                DataType = SettingDataType.Int,
                Value = AppSettings.ClientId,
                DefaultValue = 99,
                Min = 1,
                Max = 999,
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 3
            },
            new()
            {
                Key = "ConnectionTimeoutSeconds",
                DisplayName = "Connection Timeout",
                Category = "IB Connection",
                Description = "Timeout in seconds to wait for IB connection",
                ControlType = SettingControlType.Number,
                DataType = SettingDataType.Int,
                Value = AppSettings.ConnectionTimeoutSeconds,
                DefaultValue = 10,
                Min = 5,
                Max = 60,
                Unit = "seconds",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 4
            },
            new()
            {
                Key = "AccountNumber",
                DisplayName = "Primary Account",
                Category = "IB Connection",
                Description = "Your IBKR account ID (e.g., U1234567 or DU1234567 for paper)",
                ControlType = SettingControlType.Text,
                DataType = SettingDataType.String,
                Value = AppSettings.AccountNumber,
                DefaultValue = "",
                ValidationPattern = "^[A-Z]{1,2}[0-9]+$",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 5
            },
            new()
            {
                Key = "SecondaryAccountNumber",
                DisplayName = "Secondary Account",
                Category = "IB Connection",
                Description = "Secondary account for dual-account hedging",
                ControlType = SettingControlType.Text,
                DataType = SettingDataType.String,
                Value = AppSettings.SecondaryAccountNumber,
                DefaultValue = "",
                ValidationPattern = "^[A-Z]{1,2}[0-9]+$",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 6
            },
            new()
            {
                Key = "DualAccountHedgingEnabled",
                DisplayName = "Dual-Account Hedging",
                Category = "IB Connection",
                Description = "Enable LONG on primary, SHORT on secondary account",
                ControlType = SettingControlType.Toggle,
                DataType = SettingDataType.Bool,
                Value = AppSettings.DualAccountHedgingEnabled,
                DefaultValue = false,
                Order = 7
            },
            
            // ===== Timezone Settings =====
            new()
            {
                Key = "Timezone",
                DisplayName = "Timezone",
                Category = "Display",
                Description = "Your local timezone for time display",
                ControlType = SettingControlType.Select,
                DataType = SettingDataType.Enum,
                Value = AppSettings.Timezone.ToString(),
                DefaultValue = "EST",
                Options =
                [
                    new() { Value = "EST", Label = "Eastern Time (EST)" },
                    new() { Value = "CST", Label = "Central Time (CST)" },
                    new() { Value = "MST", Label = "Mountain Time (MST)" },
                    new() { Value = "PST", Label = "Pacific Time (PST)" }
                ],
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 1
            },
            
            // ===== Backend Mode Settings =====
            new()
            {
                Key = "SilentMode",
                DisplayName = "Silent Mode",
                Category = "Display",
                Description = "Suppress most console output, only show minimal heartbeat",
                ControlType = SettingControlType.Toggle,
                DataType = SettingDataType.Bool,
                Value = AppSettings.SilentMode,
                DefaultValue = false,
                Order = 2
            },
            new()
            {
                Key = "ShowBacktestDailyDetails",
                DisplayName = "Show Backtest Details",
                Category = "Display",
                Description = "Show individual trade details per day in backtest output",
                ControlType = SettingControlType.Toggle,
                DataType = SettingDataType.Bool,
                Value = AppSettings.ShowBacktestDailyDetails,
                DefaultValue = false,
                Order = 3
            },
            
            // ===== Web Frontend Settings =====
            new()
            {
                Key = "WebFrontendUrl",
                DisplayName = "Web Frontend URL",
                Category = "Web Integration",
                Description = "URL of the web frontend for live data streaming",
                ControlType = SettingControlType.Url,
                DataType = SettingDataType.String,
                Value = AppSettings.WebFrontendUrl,
                DefaultValue = "http://localhost:5114",
                Order = 1
            },
            
            // ===== Risk Management Settings =====
            new()
            {
                Key = "UseStopLoss",
                DisplayName = "Use Stop Loss",
                Category = "Risk Management",
                Description = "Enable stop loss orders in autonomous trading",
                ControlType = SettingControlType.Toggle,
                DataType = SettingDataType.Bool,
                Value = AppSettings.UseStopLoss,
                DefaultValue = false,
                Order = 1
            },
            new()
            {
                Key = "UseTrailingStopLoss",
                DisplayName = "Use Trailing Stop",
                Category = "Risk Management",
                Description = "Enable trailing stop loss in autonomous trading",
                ControlType = SettingControlType.Toggle,
                DataType = SettingDataType.Bool,
                Value = AppSettings.UseTrailingStopLoss,
                DefaultValue = false,
                Order = 2
            },
            
            // ===== Indicator Settings =====
            new()
            {
                Key = "MaxCandlesticks",
                DisplayName = "Max Candlesticks",
                Category = "Indicators",
                Description = "Maximum candlesticks to retain for indicator calculations",
                ControlType = SettingControlType.Slider,
                DataType = SettingDataType.Int,
                Value = AppSettings.MaxCandlesticks,
                DefaultValue = 255,
                Min = 50,
                Max = 500,
                Step = 5,
                Unit = "candles",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 1
            },
            
            // ===== Timing Settings =====
            new()
            {
                Key = "HeartbeatMinutes",
                DisplayName = "Heartbeat Interval",
                Category = "Timing",
                Description = "Interval between connection heartbeat checks",
                ControlType = SettingControlType.Number,
                DataType = SettingDataType.Int,
                Value = (int)AppSettings.Heartbeat.TotalMinutes,
                DefaultValue = 5,
                Min = 1,
                Max = 30,
                Unit = "minutes",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 1
            },
            new()
            {
                Key = "IpcPingIntervalSeconds",
                DisplayName = "IPC Ping Interval",
                Category = "Timing",
                Description = "Interval between IPC ping messages",
                ControlType = SettingControlType.Number,
                DataType = SettingDataType.Int,
                Value = AppSettings.IpcPingIntervalSeconds,
                DefaultValue = 300,
                Min = 30,
                Max = 600,
                Unit = "seconds",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 2
            },
            new()
            {
                Key = "TickerPriceCheckIntervalSeconds",
                DisplayName = "Price Check Interval",
                Category = "Timing",
                Description = "Interval between ticker price reports (0 to disable)",
                ControlType = SettingControlType.Number,
                DataType = SettingDataType.Int,
                Value = AppSettings.TickerPriceCheckIntervalSeconds,
                DefaultValue = 300,
                Min = 0,
                Max = 600,
                Unit = "seconds",
                IsReadOnly = true,
                RequiresRestart = true,
                Order = 3
            }
        };
        
        _cachedBundle = new SettingsBundle
        {
            AppVersion = "1.0.0",
            LastModified = DateTime.UtcNow,
            Settings = settings
        };
        
        return _cachedBundle;
    }
    
    /// <summary>
    /// Updates a runtime setting value (non-const settings only).
    /// </summary>
    public bool UpdateSetting(string key, object value)
    {
        var result = key switch
        {
            "DualAccountHedgingEnabled" when value is bool b => SetValue(() => AppSettings.DualAccountHedgingEnabled = b),
            "SilentMode" when value is bool b => SetValue(() => AppSettings.SilentMode = b),
            "ShowBacktestDailyDetails" when value is bool b => SetValue(() => AppSettings.ShowBacktestDailyDetails = b),
            "WebFrontendUrl" when value is string s => SetValue(() => AppSettings.WebFrontendUrl = s),
            "UseStopLoss" when value is bool b => SetValue(() => AppSettings.UseStopLoss = b),
            "UseTrailingStopLoss" when value is bool b => SetValue(() => AppSettings.UseTrailingStopLoss = b),
            _ => false
        };
        
        if (result)
        {
            // Update cached value
            var setting = _cachedBundle?.Settings.FirstOrDefault(s => s.Key == key);
            if (setting != null)
            {
                setting.Value = value;
                if (_cachedBundle != null)
                    _cachedBundle.LastModified = DateTime.UtcNow;
            }
        }
        
        return result;
    }
    
    private static bool SetValue(Action setter)
    {
        try
        {
            setter();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Resets a setting to its default value.
    /// </summary>
    public bool ResetToDefault(string key)
    {
        var setting = _cachedBundle?.Settings.FirstOrDefault(s => s.Key == key);
        if (setting?.DefaultValue == null || setting.IsReadOnly)
            return false;
            
        return UpdateSetting(key, setting.DefaultValue);
    }
    
    /// <summary>
    /// Exports current settings to JSON.
    /// </summary>
    public string ExportToJson()
    {
        var bundle = GetAllSettings();
        return System.Text.Json.JsonSerializer.Serialize(bundle, new System.Text.Json.JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }
}
