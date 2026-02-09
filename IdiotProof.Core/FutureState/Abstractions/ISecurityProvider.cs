// IdiotProof.Core.FutureState.Abstractions
// Security Provider Interface
// Unified security abstraction across all platforms

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IdiotProof.Core.FutureState.Security;

namespace IdiotProof.Core.FutureState.Abstractions;

/// <summary>
/// Authentication state.
/// </summary>
public enum AuthenticationState
{
    NotAuthenticated,
    Authenticating,
    Authenticated,
    TokenExpired,
    AuthenticationFailed
}

/// <summary>
/// Biometric authentication type.
/// </summary>
public enum BiometricType
{
    None,
    Fingerprint,
    FaceId,
    Iris,
    Voice
}

/// <summary>
/// Authenticated user information.
/// </summary>
public class AuthenticatedUser
{
    public string UserId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string[] Roles { get; init; } = Array.Empty<string>();
    public string[] Permissions { get; init; } = Array.Empty<string>();
    public DateTime TokenExpiresAt { get; init; }
    public Dictionary<string, string> Claims { get; init; } = new();
}

/// <summary>
/// Authentication result.
/// </summary>
public class AuthenticationResult
{
    public bool IsSuccess { get; init; }
    public AuthenticatedUser? User { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static AuthenticationResult Success(AuthenticatedUser user, string accessToken, string? refreshToken = null) =>
        new() { IsSuccess = true, User = user, AccessToken = accessToken, RefreshToken = refreshToken };
        
    public static AuthenticationResult Failure(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>
/// Security provider interface.
/// Handles authentication, authorization, and security operations.
/// </summary>
public interface ISecurityProvider
{
    /// <summary>
    /// Current authentication state.
    /// </summary>
    AuthenticationState State { get; }
    
    /// <summary>
    /// Current authenticated user (null if not authenticated).
    /// </summary>
    AuthenticatedUser? CurrentUser { get; }
    
    /// <summary>
    /// Event when authentication state changes.
    /// </summary>
    event EventHandler<AuthenticationState>? StateChanged;
    
    /// <summary>
    /// Event when the token is refreshed.
    /// </summary>
    event EventHandler<string>? TokenRefreshed;
    
    #region Authentication
    
    /// <summary>
    /// Authenticates with username/password.
    /// </summary>
    Task<AuthenticationResult> AuthenticateAsync(
        string username, 
        string password, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Authenticates with API key.
    /// </summary>
    Task<AuthenticationResult> AuthenticateWithApiKeyAsync(
        string apiKey, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Authenticates with OAuth/OIDC.
    /// </summary>
    Task<AuthenticationResult> AuthenticateWithOAuthAsync(
        string provider, // "google", "microsoft", etc.
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes the current access token.
    /// </summary>
    Task<AuthenticationResult> RefreshTokenAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs out the current user.
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if the current token is valid.
    /// </summary>
    Task<bool> ValidateTokenAsync(CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Biometrics
    
    /// <summary>
    /// Whether biometric authentication is available on this device.
    /// </summary>
    bool IsBiometricAvailable { get; }
    
    /// <summary>
    /// Type of biometric available.
    /// </summary>
    BiometricType AvailableBiometricType { get; }
    
    /// <summary>
    /// Enables biometric authentication for the current user.
    /// </summary>
    Task<bool> EnableBiometricAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Authenticates using biometrics.
    /// </summary>
    Task<AuthenticationResult> AuthenticateWithBiometricAsync(
        string promptMessage = "Authenticate to continue",
        CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Authorization
    
    /// <summary>
    /// Checks if the current user has a specific permission.
    /// </summary>
    bool HasPermission(string permission);
    
    /// <summary>
    /// Checks if the current user has a specific role.
    /// </summary>
    bool HasRole(string role);
    
    /// <summary>
    /// Checks if the current user can perform an action on a resource.
    /// </summary>
    Task<bool> CanAccessAsync(string resource, string action, CancellationToken cancellationToken = default);
    
    #endregion
    
    #region Security Operations
    
    /// <summary>
    /// Gets the current access token for API calls.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates request metadata with authentication.
    /// </summary>
    RequestMetadata CreateAuthenticatedMetadata();
    
    /// <summary>
    /// Validates and sanitizes user input.
    /// </summary>
    InputSanitizer.ValidationResult ValidateInput(string input, InputType inputType);
    
    /// <summary>
    /// Rate limiter for the current client.
    /// </summary>
    IRateLimiter RateLimiter { get; }
    
    #endregion
}

/// <summary>
/// Input types for validation.
/// </summary>
public enum InputType
{
    Ticker,
    StrategyName,
    IdiotScript,
    ClientId,
    Email,
    Password,
    Price,
    Quantity,
    Generic
}

/// <summary>
/// Security configuration.
/// </summary>
public class SecurityConfiguration
{
    /// <summary>
    /// IdiotProof API base URL.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// OAuth configuration.
    /// </summary>
    public OAuthConfiguration? OAuth { get; set; }
    
    /// <summary>
    /// TLS configuration.
    /// </summary>
    public TlsConfiguration TlsConfig { get; set; } = new();
    
    /// <summary>
    /// Rate limit configuration.
    /// </summary>
    public RateLimitConfiguration RateLimitConfig { get; set; } = RateLimitPolicies.Default;
    
    /// <summary>
    /// Token refresh threshold (refresh when this much time remains).
    /// </summary>
    public TimeSpan TokenRefreshThreshold { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Whether to persist authentication across app restarts.
    /// </summary>
    public bool PersistAuthentication { get; set; } = true;
    
    /// <summary>
    /// Maximum idle time before requiring re-authentication.
    /// </summary>
    public TimeSpan? MaxIdleTime { get; set; } = TimeSpan.FromHours(8);
}

/// <summary>
/// OAuth/OIDC configuration.
/// </summary>
public class OAuthConfiguration
{
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = { "openid", "profile", "email" };
    public string RedirectUri { get; set; } = string.Empty;
    public string PostLogoutRedirectUri { get; set; } = string.Empty;
}

/// <summary>
/// Common permissions used in IdiotProof.
/// </summary>
public static class Permissions
{
    // Trading permissions
    public const string TradingRead = "trading:read";
    public const string TradingWrite = "trading:write";
    
    // Strategy permissions
    public const string StrategyRead = "strategy:read";
    public const string StrategyWrite = "strategy:write";
    public const string StrategyExecute = "strategy:execute";
    
    // Market data permissions
    public const string MarketDataRealtime = "marketdata:realtime";
    public const string MarketDataHistorical = "marketdata:historical";
    
    // Account permissions
    public const string AccountRead = "account:read";
    public const string AccountWrite = "account:write";
    
    // Admin permissions
    public const string AdminUsers = "admin:users";
    public const string AdminSettings = "admin:settings";
}

/// <summary>
/// Common roles used in IdiotProof.
/// </summary>
public static class Roles
{
    public const string User = "user";
    public const string PremiumUser = "premium";
    public const string Trader = "trader";
    public const string Admin = "admin";
    public const string ServiceAccount = "service";
}

/// <summary>
/// Security provider factory.
/// </summary>
public interface ISecurityProviderFactory
{
    /// <summary>
    /// Creates a security provider with the given configuration.
    /// </summary>
    ISecurityProvider Create(SecurityConfiguration configuration);
}

/// <summary>
/// Extension methods for security provider.
/// </summary>
public static class SecurityProviderExtensions
{
    /// <summary>
    /// Ensures the user is authenticated, throws if not.
    /// </summary>
    public static void EnsureAuthenticated(this ISecurityProvider provider)
    {
        if (provider.State != AuthenticationState.Authenticated)
        {
            throw new UnauthorizedAccessException("User is not authenticated");
        }
    }
    
    /// <summary>
    /// Ensures the user has a specific permission, throws if not.
    /// </summary>
    public static void EnsurePermission(this ISecurityProvider provider, string permission)
    {
        provider.EnsureAuthenticated();
        
        if (!provider.HasPermission(permission))
        {
            throw new UnauthorizedAccessException($"Missing required permission: {permission}");
        }
    }
    
    /// <summary>
    /// Ensures the user has a specific role, throws if not.
    /// </summary>
    public static void EnsureRole(this ISecurityProvider provider, string role)
    {
        provider.EnsureAuthenticated();
        
        if (!provider.HasRole(role))
        {
            throw new UnauthorizedAccessException($"Missing required role: {role}");
        }
    }
}
