// IdiotProof.Core.FutureState.Abstractions
// Platform Services Abstraction
// Provides platform-specific implementations for different targets

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Core.FutureState.Abstractions;

/// <summary>
/// Platform types supported by IdiotProof.
/// </summary>
public enum PlatformType
{
    Unknown,
    Windows,
    MacOS,
    Linux,
    iOS,
    Android,
    WebBrowser,
    WebAssembly
}

/// <summary>
/// Runtime environment.
/// </summary>
public enum RuntimeEnvironment
{
    Development,
    Staging,
    Production
}

/// <summary>
/// Platform information.
/// </summary>
public class PlatformInfo
{
    public PlatformType Platform { get; init; }
    public string OSVersion { get; init; } = string.Empty;
    public string RuntimeVersion { get; init; } = string.Empty;
    public string DeviceModel { get; init; } = string.Empty;
    public bool Is64Bit { get; init; }
    public bool IsDesktop { get; init; }
    public bool IsMobile { get; init; }
    public bool IsBrowser { get; init; }
    public string[] SupportedFeatures { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Storage capabilities per platform.
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Whether secure storage (Keychain, Credential Manager) is available.
    /// </summary>
    bool SupportsSecureStorage { get; }
    
    /// <summary>
    /// Local app data directory.
    /// </summary>
    string AppDataDirectory { get; }
    
    /// <summary>
    /// Cache directory.
    /// </summary>
    string CacheDirectory { get; }
    
    /// <summary>
    /// Saves data to local storage.
    /// </summary>
    Task SaveAsync(string key, string data, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Loads data from local storage.
    /// </summary>
    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes data from local storage.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Saves sensitive data securely (encrypted).
    /// </summary>
    Task SaveSecureAsync(string key, string data, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Loads sensitive data securely.
    /// </summary>
    Task<string?> LoadSecureAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Saves a file.
    /// </summary>
    Task SaveFileAsync(string path, Stream content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    Task<Stream?> OpenFileAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a file exists.
    /// </summary>
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notification capabilities per platform.
/// </summary>
public interface INotificationProvider
{
    /// <summary>
    /// Whether push notifications are supported.
    /// </summary>
    bool SupportsPushNotifications { get; }
    
    /// <summary>
    /// Whether local notifications are supported.
    /// </summary>
    bool SupportsLocalNotifications { get; }
    
    /// <summary>
    /// Requests notification permissions.
    /// </summary>
    Task<bool> RequestPermissionAsync();
    
    /// <summary>
    /// Shows a local notification.
    /// </summary>
    Task ShowLocalNotificationAsync(
        string title,
        string body,
        Dictionary<string, string>? data = null);
    
    /// <summary>
    /// Registers for push notifications.
    /// </summary>
    Task<string?> RegisterForPushAsync();
    
    /// <summary>
    /// Event when a notification is received.
    /// </summary>
    event EventHandler<NotificationReceivedEventArgs>? NotificationReceived;
    
    /// <summary>
    /// Event when a notification is tapped.
    /// </summary>
    event EventHandler<NotificationTappedEventArgs>? NotificationTapped;
}

public class NotificationReceivedEventArgs : EventArgs
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public Dictionary<string, string> Data { get; init; } = new();
}

public class NotificationTappedEventArgs : EventArgs
{
    public string NotificationId { get; init; } = string.Empty;
    public Dictionary<string, string> Data { get; init; } = new();
}

/// <summary>
/// Logging abstraction per platform.
/// </summary>
public interface ILoggingProvider
{
    /// <summary>
    /// Log levels to include.
    /// </summary>
    LogLevel MinimumLevel { get; set; }
    
    void Log(LogLevel level, string message, Exception? exception = null, Dictionary<string, object>? properties = null);
    void Debug(string message, Dictionary<string, object>? properties = null);
    void Info(string message, Dictionary<string, object>? properties = null);
    void Warning(string message, Dictionary<string, object>? properties = null);
    void Error(string message, Exception? exception = null, Dictionary<string, object>? properties = null);
    void Critical(string message, Exception? exception = null, Dictionary<string, object>? properties = null);
    
    /// <summary>
    /// Creates a scoped logger with contextual properties.
    /// </summary>
    ILoggingProvider CreateScope(Dictionary<string, object> properties);
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Analytics/telemetry abstraction.
/// </summary>
public interface IAnalyticsProvider
{
    /// <summary>
    /// Whether analytics is enabled.
    /// </summary>
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// Tracks an event.
    /// </summary>
    void TrackEvent(string eventName, Dictionary<string, string>? properties = null);
    
    /// <summary>
    /// Tracks a metric.
    /// </summary>
    void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null);
    
    /// <summary>
    /// Tracks an exception.
    /// </summary>
    void TrackException(Exception exception, Dictionary<string, string>? properties = null);
    
    /// <summary>
    /// Sets user identity for tracking.
    /// </summary>
    void SetUserId(string userId);
    
    /// <summary>
    /// Sets user properties.
    /// </summary>
    void SetUserProperties(Dictionary<string, string> properties);
    
    /// <summary>
    /// Starts a timed operation.
    /// </summary>
    IDisposable StartTimedOperation(string operationName, Dictionary<string, string>? properties = null);
}

/// <summary>
/// Connectivity information.
/// </summary>
public interface IConnectivityProvider
{
    /// <summary>
    /// Current network status.
    /// </summary>
    NetworkStatus Status { get; }
    
    /// <summary>
    /// Connection type (WiFi, Cellular, etc.).
    /// </summary>
    ConnectionType ConnectionType { get; }
    
    /// <summary>
    /// Whether metered connection (cellular).
    /// </summary>
    bool IsMetered { get; }
    
    /// <summary>
    /// Event when connectivity changes.
    /// </summary>
    event EventHandler<NetworkStatus>? StatusChanged;
}

public enum NetworkStatus
{
    Unknown,
    None,
    Limited,
    Internet
}

public enum ConnectionType
{
    Unknown,
    None,
    WiFi,
    Cellular,
    Ethernet,
    Bluetooth
}

/// <summary>
/// Threading/dispatcher abstraction for UI updates.
/// </summary>
public interface IDispatcherProvider
{
    /// <summary>
    /// Whether currently on main/UI thread.
    /// </summary>
    bool IsMainThread { get; }
    
    /// <summary>
    /// Executes on main thread.
    /// </summary>
    void RunOnMainThread(Action action);
    
    /// <summary>
    /// Executes on main thread async.
    /// </summary>
    Task RunOnMainThreadAsync(Func<Task> action);
    
    /// <summary>
    /// Executes on background thread.
    /// </summary>
    Task RunOnBackgroundAsync(Func<Task> action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Main platform services interface.
/// Combines all platform-specific services.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Platform information.
    /// </summary>
    PlatformInfo PlatformInfo { get; }
    
    /// <summary>
    /// Current runtime environment.
    /// </summary>
    RuntimeEnvironment Environment { get; }
    
    /// <summary>
    /// Storage provider.
    /// </summary>
    IStorageProvider Storage { get; }
    
    /// <summary>
    /// Notification provider.
    /// </summary>
    INotificationProvider Notifications { get; }
    
    /// <summary>
    /// Logging provider.
    /// </summary>
    ILoggingProvider Logging { get; }
    
    /// <summary>
    /// Analytics provider.
    /// </summary>
    IAnalyticsProvider Analytics { get; }
    
    /// <summary>
    /// Connectivity provider.
    /// </summary>
    IConnectivityProvider Connectivity { get; }
    
    /// <summary>
    /// Dispatcher for UI thread operations.
    /// </summary>
    IDispatcherProvider Dispatcher { get; }
    
    /// <summary>
    /// Checks if a feature is supported on this platform.
    /// </summary>
    bool IsFeatureSupported(string featureName);
    
    /// <summary>
    /// Gets platform-specific configuration.
    /// </summary>
    T? GetPlatformConfig<T>(string key);
}

/// <summary>
/// Feature names for platform capability checks.
/// </summary>
public static class PlatformFeatures
{
    public const string SecureStorage = "SecureStorage";
    public const string Biometrics = "Biometrics";
    public const string PushNotifications = "PushNotifications";
    public const string LocalNotifications = "LocalNotifications";
    public const string BackgroundExecution = "BackgroundExecution";
    public const string FileSystem = "FileSystem";
    public const string Camera = "Camera";
    public const string Location = "Location";
    public const string Bluetooth = "Bluetooth";
    public const string NFC = "NFC";
    public const string Haptics = "Haptics";
    public const string WebSockets = "WebSockets";
    public const string gRPC = "gRPC";
    public const string Http2 = "Http2";
}

/// <summary>
/// Registry for platform service implementations.
/// </summary>
public static class PlatformServiceRegistry
{
    private static IPlatformServices? _current;
    
    /// <summary>
    /// Gets the current platform services instance.
    /// </summary>
    public static IPlatformServices Current => 
        _current ?? throw new InvalidOperationException(
            "Platform services not initialized. Call PlatformServiceRegistry.Initialize() first.");
    
    /// <summary>
    /// Initializes with platform-specific implementation.
    /// Call this during app startup.
    /// </summary>
    public static void Initialize(IPlatformServices platformServices)
    {
        _current = platformServices ?? throw new ArgumentNullException(nameof(platformServices));
    }
    
    /// <summary>
    /// Whether platform services are initialized.
    /// </summary>
    public static bool IsInitialized => _current != null;
}
