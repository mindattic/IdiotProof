// IdiotProof.Core.FutureState.CrossPlatform
// Feature Flags for Platform-Specific and A/B Testing
// Enables gradual rollouts and platform-specific features

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IdiotProof.Core.FutureState.Abstractions;

namespace IdiotProof.Core.FutureState.CrossPlatform;

/// <summary>
/// Feature flag evaluation result.
/// </summary>
public class FeatureFlagResult
{
    public bool IsEnabled { get; init; }
    public string? Variant { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public static FeatureFlagResult Enabled(string? variant = null) =>
        new() { IsEnabled = true, Variant = variant };
        
    public static FeatureFlagResult Disabled() =>
        new() { IsEnabled = false };
}

/// <summary>
/// Feature flag definition.
/// </summary>
public class FeatureFlag
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool DefaultValue { get; init; }
    public FeatureFlagType Type { get; init; } = FeatureFlagType.Boolean;
    
    /// <summary>
    /// Percentage rollout (0-100). Only applies if Type is Percentage.
    /// </summary>
    public int Percentage { get; init; } = 100;
    
    /// <summary>
    /// Platforms where this feature is enabled.
    /// Empty = all platforms.
    /// </summary>
    public PlatformType[] EnabledPlatforms { get; init; } = Array.Empty<PlatformType>();
    
    /// <summary>
    /// Roles required to access this feature.
    /// Empty = no role restriction.
    /// </summary>
    public string[] RequiredRoles { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Variants for A/B testing.
    /// </summary>
    public string[] Variants { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Start time for time-based flags.
    /// </summary>
    public DateTime? EnabledFrom { get; init; }
    
    /// <summary>
    /// End time for time-based flags.
    /// </summary>
    public DateTime? EnabledUntil { get; init; }
}

public enum FeatureFlagType
{
    /// <summary>Simple on/off flag.</summary>
    Boolean,
    
    /// <summary>Percentage-based rollout.</summary>
    Percentage,
    
    /// <summary>A/B test with variants.</summary>
    Experiment,
    
    /// <summary>Time-based flag.</summary>
    Scheduled
}

/// <summary>
/// Context for evaluating feature flags.
/// </summary>
public class FeatureFlagContext
{
    public string? UserId { get; init; }
    public string? ClientId { get; init; }
    public string[]? Roles { get; init; }
    public PlatformType Platform { get; init; }
    public Dictionary<string, object> CustomAttributes { get; init; } = new();
}

/// <summary>
/// Feature flag service interface.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Evaluates whether a feature is enabled.
    /// </summary>
    FeatureFlagResult Evaluate(string flagName, FeatureFlagContext? context = null);
    
    /// <summary>
    /// Gets all feature flag definitions.
    /// </summary>
    IReadOnlyDictionary<string, FeatureFlag> GetAllFlags();
    
    /// <summary>
    /// Refreshes flags from remote source.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Registers a local override (for testing).
    /// </summary>
    void SetOverride(string flagName, bool value);
    
    /// <summary>
    /// Clears all local overrides.
    /// </summary>
    void ClearOverrides();
}

/// <summary>
/// In-memory feature flag service implementation.
/// </summary>
public class FeatureFlagService : IFeatureFlagService
{
    private readonly Dictionary<string, FeatureFlag> _flags = new();
    private readonly Dictionary<string, bool> _overrides = new();
    private readonly object _lock = new();
    
    public FeatureFlagService()
    {
        RegisterDefaultFlags();
    }
    
    public FeatureFlagResult Evaluate(string flagName, FeatureFlagContext? context = null)
    {
        // Check overrides first (for testing)
        lock (_lock)
        {
            if (_overrides.TryGetValue(flagName, out var overrideValue))
            {
                return overrideValue ? FeatureFlagResult.Enabled() : FeatureFlagResult.Disabled();
            }
        }
        
        if (!_flags.TryGetValue(flagName, out var flag))
        {
            // Unknown flag - return disabled
            return FeatureFlagResult.Disabled();
        }
        
        context ??= new FeatureFlagContext
        {
            Platform = PlatformDetector.CurrentPlatform
        };
        
        return EvaluateFlag(flag, context);
    }
    
    public IReadOnlyDictionary<string, FeatureFlag> GetAllFlags()
    {
        lock (_lock)
        {
            return new Dictionary<string, FeatureFlag>(_flags);
        }
    }
    
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // In production, fetch from remote config service
        // For now, just return completed
        return Task.CompletedTask;
    }
    
    public void SetOverride(string flagName, bool value)
    {
        lock (_lock)
        {
            _overrides[flagName] = value;
        }
    }
    
    public void ClearOverrides()
    {
        lock (_lock)
        {
            _overrides.Clear();
        }
    }
    
    private FeatureFlagResult EvaluateFlag(FeatureFlag flag, FeatureFlagContext context)
    {
        // Check time-based constraints
        if (flag.EnabledFrom.HasValue && DateTime.UtcNow < flag.EnabledFrom.Value)
            return FeatureFlagResult.Disabled();
            
        if (flag.EnabledUntil.HasValue && DateTime.UtcNow > flag.EnabledUntil.Value)
            return FeatureFlagResult.Disabled();
        
        // Check platform constraints
        if (flag.EnabledPlatforms.Length > 0)
        {
            if (!Array.Exists(flag.EnabledPlatforms, p => p == context.Platform))
            {
                return FeatureFlagResult.Disabled();
            }
        }
        
        // Check role constraints
        if (flag.RequiredRoles.Length > 0)
        {
            if (context.Roles == null || context.Roles.Length == 0)
                return FeatureFlagResult.Disabled();
                
            var hasRequiredRole = false;
            foreach (var required in flag.RequiredRoles)
            {
                if (Array.Exists(context.Roles, r => r.Equals(required, StringComparison.OrdinalIgnoreCase)))
                {
                    hasRequiredRole = true;
                    break;
                }
            }
            
            if (!hasRequiredRole)
                return FeatureFlagResult.Disabled();
        }
        
        // Evaluate based on type
        return flag.Type switch
        {
            FeatureFlagType.Boolean => flag.DefaultValue 
                ? FeatureFlagResult.Enabled() 
                : FeatureFlagResult.Disabled(),
                
            FeatureFlagType.Percentage => EvaluatePercentage(flag, context),
            FeatureFlagType.Experiment => EvaluateExperiment(flag, context),
            FeatureFlagType.Scheduled => flag.DefaultValue 
                ? FeatureFlagResult.Enabled() 
                : FeatureFlagResult.Disabled(),
                
            _ => FeatureFlagResult.Disabled()
        };
    }
    
    private FeatureFlagResult EvaluatePercentage(FeatureFlag flag, FeatureFlagContext context)
    {
        // Use consistent hashing based on user ID for sticky assignment
        var hash = GetConsistentHash(flag.Name, context.UserId ?? context.ClientId ?? "anonymous");
        var bucket = Math.Abs(hash) % 100;
        
        return bucket < flag.Percentage 
            ? FeatureFlagResult.Enabled() 
            : FeatureFlagResult.Disabled();
    }
    
    private FeatureFlagResult EvaluateExperiment(FeatureFlag flag, FeatureFlagContext context)
    {
        if (flag.Variants.Length == 0)
            return FeatureFlagResult.Disabled();
        
        // Consistent variant assignment based on user
        var hash = GetConsistentHash(flag.Name, context.UserId ?? context.ClientId ?? "anonymous");
        var variantIndex = Math.Abs(hash) % flag.Variants.Length;
        
        return FeatureFlagResult.Enabled(flag.Variants[variantIndex]);
    }
    
    private static int GetConsistentHash(string flagName, string userId)
    {
        // Simple consistent hash - replace with MurmurHash3 for production
        var combined = $"{flagName}:{userId}";
        return combined.GetHashCode();
    }
    
    private void RegisterDefaultFlags()
    {
        // Core features
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.UseGrpc,
            Description = "Use gRPC transport instead of REST",
            DefaultValue = true,
            Type = FeatureFlagType.Boolean,
            EnabledPlatforms = new[] 
            { 
                PlatformType.Windows, 
                PlatformType.MacOS, 
                PlatformType.Linux,
                PlatformType.iOS,
                PlatformType.Android
            }
        });
        
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.AutonomousTrading,
            Description = "Enable AI-driven autonomous trading",
            DefaultValue = true,
            Type = FeatureFlagType.Boolean
        });
        
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.AdaptiveOrder,
            Description = "Enable adaptive order management",
            DefaultValue = true,
            Type = FeatureFlagType.Boolean
        });
        
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.LstmPrediction,
            Description = "Enable LSTM neural network predictions",
            DefaultValue = true,
            Type = FeatureFlagType.Percentage,
            Percentage = 100
        });
        
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.BiometricAuth,
            Description = "Enable biometric authentication",
            DefaultValue = true,
            Type = FeatureFlagType.Boolean,
            EnabledPlatforms = new[]
            {
                PlatformType.iOS,
                PlatformType.Android,
                PlatformType.MacOS
            }
        });
        
        // Experimental features
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.NewDashboard,
            Description = "New dashboard UI experiment",
            DefaultValue = false,
            Type = FeatureFlagType.Experiment,
            Variants = new[] { "control", "variant_a", "variant_b" }
        });
        
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.RealTimeCharts,
            Description = "Real-time chart updates",
            DefaultValue = true,
            Type = FeatureFlagType.Boolean
        });
        
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.PushNotifications,
            Description = "Push notifications for alerts",
            DefaultValue = true,
            Type = FeatureFlagType.Boolean,
            EnabledPlatforms = new[]
            {
                PlatformType.iOS,
                PlatformType.Android
            }
        });
        
        // Admin features
        RegisterFlag(new FeatureFlag
        {
            Name = FeatureFlags.DebugMode,
            Description = "Enable debug logging and tools",
            DefaultValue = false,
            Type = FeatureFlagType.Boolean,
            RequiredRoles = new[] { "admin", "developer" }
        });
    }
    
    private void RegisterFlag(FeatureFlag flag)
    {
        _flags[flag.Name] = flag;
    }
}

/// <summary>
/// Standard feature flag names.
/// </summary>
public static class FeatureFlags
{
    // Transport
    public const string UseGrpc = "use_grpc";
    public const string UseWebSocket = "use_websocket";
    public const string EnableCompression = "enable_compression";
    
    // Trading features
    public const string AutonomousTrading = "autonomous_trading";
    public const string AdaptiveOrder = "adaptive_order";
    public const string LstmPrediction = "lstm_prediction";
    public const string TickerLearning = "ticker_learning";
    
    // Security
    public const string BiometricAuth = "biometric_auth";
    public const string TwoFactorAuth = "two_factor_auth";
    public const string ApiKeyAuth = "api_key_auth";
    
    // UI features
    public const string NewDashboard = "new_dashboard";
    public const string RealTimeCharts = "realtime_charts";
    public const string DarkMode = "dark_mode";
    
    // Notifications
    public const string PushNotifications = "push_notifications";
    public const string EmailAlerts = "email_alerts";
    
    // Debug
    public const string DebugMode = "debug_mode";
    public const string PerformanceMetrics = "performance_metrics";
}

/// <summary>
/// Extension methods for feature flags.
/// </summary>
public static class FeatureFlagExtensions
{
    /// <summary>
    /// Shorthand for checking if a feature is enabled.
    /// </summary>
    public static bool IsEnabled(this IFeatureFlagService service, string flagName) =>
        service.Evaluate(flagName).IsEnabled;
    
    /// <summary>
    /// Shorthand for checking if a feature is enabled with context.
    /// </summary>
    public static bool IsEnabled(this IFeatureFlagService service, string flagName, FeatureFlagContext context) =>
        service.Evaluate(flagName, context).IsEnabled;
    
    /// <summary>
    /// Gets the variant for an A/B test feature.
    /// </summary>
    public static string? GetVariant(this IFeatureFlagService service, string flagName, FeatureFlagContext? context = null) =>
        service.Evaluate(flagName, context).Variant;
}
