// IdiotProof.Core.FutureState.CrossPlatform
// Platform Detection and Capability Checking
// Detects runtime platform and available features

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using IdiotProof.Core.FutureState.Abstractions;

namespace IdiotProof.Core.FutureState.CrossPlatform;

/// <summary>
/// Detects the current runtime platform and its capabilities.
/// </summary>
public static class PlatformDetector
{
    private static PlatformInfo? cachedInfo;
    
    /// <summary>
    /// Gets information about the current platform.
    /// </summary>
    public static PlatformInfo GetPlatformInfo()
    {
        if (cachedInfo != null)
            return cachedInfo;
            
        cachedInfo = DetectPlatform();
        return cachedInfo;
    }
    
    /// <summary>
    /// Gets the current platform type.
    /// </summary>
    public static PlatformType CurrentPlatform => GetPlatformInfo().Platform;
    
    /// <summary>
    /// Whether running on a desktop platform.
    /// </summary>
    public static bool IsDesktop => GetPlatformInfo().IsDesktop;
    
    /// <summary>
    /// Whether running on a mobile platform.
    /// </summary>
    public static bool IsMobile => GetPlatformInfo().IsMobile;
    
    /// <summary>
    /// Whether running in a browser (WASM).
    /// </summary>
    public static bool IsBrowser => GetPlatformInfo().IsBrowser;
    
    /// <summary>
    /// Checks if a specific feature is supported on the current platform.
    /// </summary>
    public static bool IsFeatureSupported(string featureName)
    {
        var info = GetPlatformInfo();
        return Array.Exists(info.SupportedFeatures, f => 
            f.Equals(featureName, StringComparison.OrdinalIgnoreCase));
    }
    
    private static PlatformInfo DetectPlatform()
    {
        var platform = DetectPlatformType();
        var features = DetectFeatures(platform);
        
        return new PlatformInfo
        {
            Platform = platform,
            OSVersion = GetOSVersion(),
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            DeviceModel = GetDeviceModel(),
            Is64Bit = Environment.Is64BitOperatingSystem,
            IsDesktop = platform is PlatformType.Windows or PlatformType.MacOS or PlatformType.Linux,
            IsMobile = platform is PlatformType.iOS or PlatformType.Android,
            IsBrowser = platform is PlatformType.WebBrowser or PlatformType.WebAssembly,
            SupportedFeatures = features
        };
    }
    
    private static PlatformType DetectPlatformType()
    {
        // Check for WebAssembly first (Blazor WASM)
        if (OperatingSystem.IsBrowser())
            return PlatformType.WebAssembly;
            
        // Check mobile platforms
        if (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            return PlatformType.iOS;
            
        if (OperatingSystem.IsAndroid())
            return PlatformType.Android;
            
        // Check desktop platforms
        if (OperatingSystem.IsWindows())
            return PlatformType.Windows;
            
        if (OperatingSystem.IsMacOS())
            return PlatformType.MacOS;
            
        if (OperatingSystem.IsLinux())
            return PlatformType.Linux;
            
        return PlatformType.Unknown;
    }
    
    private static string GetOSVersion()
    {
        return RuntimeInformation.OSDescription;
    }
    
    private static string GetDeviceModel()
    {
        // This would be populated by platform-specific code
        // For now, return a generic value
        return RuntimeInformation.OSArchitecture.ToString();
    }
    
    private static string[] DetectFeatures(PlatformType platform)
    {
        var features = new List<string>();
        
        // Common features available on all platforms
        features.Add(PlatformFeatures.WebSockets);
        
        switch (platform)
        {
            case PlatformType.Windows:
                features.AddRange(new[]
                {
                    PlatformFeatures.SecureStorage,
                    PlatformFeatures.FileSystem,
                    PlatformFeatures.LocalNotifications,
                    PlatformFeatures.BackgroundExecution,
                    PlatformFeatures.gRPC,
                    PlatformFeatures.Http2
                });
                break;
                
            case PlatformType.MacOS:
                features.AddRange(new[]
                {
                    PlatformFeatures.SecureStorage,  // Keychain
                    PlatformFeatures.Biometrics,     // Touch ID
                    PlatformFeatures.FileSystem,
                    PlatformFeatures.LocalNotifications,
                    PlatformFeatures.PushNotifications,
                    PlatformFeatures.BackgroundExecution,
                    PlatformFeatures.gRPC,
                    PlatformFeatures.Http2
                });
                break;
                
            case PlatformType.Linux:
                features.AddRange(new[]
                {
                    PlatformFeatures.FileSystem,
                    PlatformFeatures.BackgroundExecution,
                    PlatformFeatures.gRPC,
                    PlatformFeatures.Http2
                });
                break;
                
            case PlatformType.iOS:
                features.AddRange(new[]
                {
                    PlatformFeatures.SecureStorage,  // Keychain
                    PlatformFeatures.Biometrics,     // Face ID / Touch ID
                    PlatformFeatures.FileSystem,
                    PlatformFeatures.LocalNotifications,
                    PlatformFeatures.PushNotifications,
                    PlatformFeatures.Camera,
                    PlatformFeatures.Location,
                    PlatformFeatures.Haptics,
                    PlatformFeatures.NFC,
                    PlatformFeatures.gRPC,
                    PlatformFeatures.Http2
                });
                break;
                
            case PlatformType.Android:
                features.AddRange(new[]
                {
                    PlatformFeatures.SecureStorage,  // Keystore
                    PlatformFeatures.Biometrics,     // Fingerprint
                    PlatformFeatures.FileSystem,
                    PlatformFeatures.LocalNotifications,
                    PlatformFeatures.PushNotifications,
                    PlatformFeatures.BackgroundExecution,
                    PlatformFeatures.Camera,
                    PlatformFeatures.Location,
                    PlatformFeatures.Haptics,
                    PlatformFeatures.NFC,
                    PlatformFeatures.Bluetooth,
                    PlatformFeatures.gRPC,
                    PlatformFeatures.Http2
                });
                break;
                
            case PlatformType.WebBrowser:
            case PlatformType.WebAssembly:
                features.AddRange(new[]
                {
                    // Limited features in browser
                    PlatformFeatures.LocalNotifications, // With permission
                    PlatformFeatures.Location,           // With permission
                    // No gRPC (use gRPC-Web or REST fallback)
                    PlatformFeatures.Http2              // Browser managed
                });
                break;
        }
        
        return features.ToArray();
    }
}

/// <summary>
/// Provides recommended configuration based on platform.
/// </summary>
public static class PlatformConfiguration
{
    /// <summary>
    /// Gets the recommended transport protocol for the current platform.
    /// </summary>
    public static TransportProtocol RecommendedTransport
    {
        get
        {
            var platform = PlatformDetector.CurrentPlatform;
            
            // Browser/WASM cannot use native gRPC
            if (platform is PlatformType.WebBrowser or PlatformType.WebAssembly)
                return TransportProtocol.RestJson;  // Or gRPC-Web via separate implementation
                
            // All other platforms support gRPC
            return TransportProtocol.Grpc;
        }
    }
    
    /// <summary>
    /// Gets the fallback transport protocol.
    /// </summary>
    public static TransportProtocol FallbackTransport => TransportProtocol.RestJson;
    
    /// <summary>
    /// Whether to use secure storage for tokens.
    /// </summary>
    public static bool UseSecureStorage =>
        PlatformDetector.IsFeatureSupported(PlatformFeatures.SecureStorage);
    
    /// <summary>
    /// Whether biometric authentication is available.
    /// </summary>
    public static bool IsBiometricAvailable =>
        PlatformDetector.IsFeatureSupported(PlatformFeatures.Biometrics);
    
    /// <summary>
    /// Gets maximum recommended concurrent connections.
    /// </summary>
    public static int MaxConcurrentConnections
    {
        get
        {
            var platform = PlatformDetector.CurrentPlatform;
            
            return platform switch
            {
                // Mobile devices - conserve battery/bandwidth
                PlatformType.iOS or PlatformType.Android => 4,
                
                // Browser - browser manages connections
                PlatformType.WebBrowser or PlatformType.WebAssembly => 6,
                
                // Desktop - more resources available
                _ => 10
            };
        }
    }
    
    /// <summary>
    /// Gets recommended request timeout based on platform.
    /// </summary>
    public static TimeSpan DefaultRequestTimeout
    {
        get
        {
            var platform = PlatformDetector.CurrentPlatform;
            
            return platform switch
            {
                // Mobile - may have slower connections
                PlatformType.iOS or PlatformType.Android => TimeSpan.FromSeconds(60),
                
                // Desktop/Server - faster connections expected
                _ => TimeSpan.FromSeconds(30)
            };
        }
    }
    
    /// <summary>
    /// Gets recommended keep-alive interval.
    /// </summary>
    public static TimeSpan KeepAliveInterval
    {
        get
        {
            var platform = PlatformDetector.CurrentPlatform;
            
            return platform switch
            {
                // Mobile - less aggressive to save battery
                PlatformType.iOS or PlatformType.Android => TimeSpan.FromSeconds(60),
                
                // Desktop - more aggressive for real-time
                _ => TimeSpan.FromSeconds(30)
            };
        }
    }
    
    /// <summary>
    /// Creates a transport configuration optimized for the current platform.
    /// </summary>
    public static TransportConfiguration CreateOptimizedConfig(string serverUrl)
    {
        return new TransportConfiguration
        {
            ServerUrl = serverUrl,
            PrimaryProtocol = RecommendedTransport,
            FallbackProtocol = FallbackTransport,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = DefaultRequestTimeout,
            AutoReconnect = true,
            MaxReconnectAttempts = PlatformDetector.IsMobile ? 3 : 5,
            ReconnectDelay = TimeSpan.FromSeconds(2),
            EnableCompression = !PlatformDetector.IsBrowser, // Browser handles compression
            KeepAliveInterval = KeepAliveInterval
        };
    }
}
