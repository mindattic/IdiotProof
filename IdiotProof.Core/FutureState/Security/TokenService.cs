// IdiotProof.Core.FutureState.Security
// Token Service for JWT and API Key authentication
// Supports both stateless JWT tokens and persistent API keys

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IdiotProof.Core.FutureState.Security;

/// <summary>
/// Configuration for token service.
/// </summary>
public class TokenConfiguration
{
    /// <summary>
    /// Secret key for JWT signing (min 256 bits / 32 chars).
    /// In production, use key vault or HSM.
    /// </summary>
    public string JwtSecretKey { get; set; } = string.Empty;
    
    /// <summary>
    /// JWT issuer (your service name).
    /// </summary>
    public string Issuer { get; set; } = "IdiotProof";
    
    /// <summary>
    /// JWT audience (client applications).
    /// </summary>
    public string Audience { get; set; } = "IdiotProof.Client";
    
    /// <summary>
    /// Access token expiration.
    /// </summary>
    public TimeSpan AccessTokenExpiration { get; set; } = TimeSpan.FromHours(1);
    
    /// <summary>
    /// Refresh token expiration.
    /// </summary>
    public TimeSpan RefreshTokenExpiration { get; set; } = TimeSpan.FromDays(30);
    
    /// <summary>
    /// API key expiration (null = never expires).
    /// </summary>
    public TimeSpan? ApiKeyExpiration { get; set; }
    
    /// <summary>
    /// Maximum failed auth attempts before lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;
    
    /// <summary>
    /// Lockout duration after max failed attempts.
    /// </summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// JWT token claims.
/// </summary>
public class TokenClaims
{
    public string Subject { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string[] Roles { get; init; } = Array.Empty<string>();
    public string[] Permissions { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> CustomClaims { get; init; } = new();
    public DateTime IssuedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string TokenId { get; init; } = string.Empty;
}

/// <summary>
/// Represents an API key.
/// </summary>
public class ApiKey
{
    public string KeyId { get; init; } = string.Empty;
    public string HashedKey { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? RevokedReason { get; set; }
}

/// <summary>
/// Token validation result.
/// </summary>
public class TokenValidationResult
{
    public bool IsValid { get; init; }
    public TokenClaims? Claims { get; init; }
    public string? Error { get; init; }
    
    public static TokenValidationResult Success(TokenClaims claims) =>
        new() { IsValid = true, Claims = claims };
        
    public static TokenValidationResult Failure(string error) =>
        new() { IsValid = false, Error = error };
}

/// <summary>
/// Interface for token operations.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a JWT access token.
    /// </summary>
    string GenerateAccessToken(TokenClaims claims);
    
    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    string GenerateRefreshToken(string clientId);
    
    /// <summary>
    /// Validates a JWT token.
    /// </summary>
    TokenValidationResult ValidateToken(string token);
    
    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    (string accessToken, string refreshToken)? RefreshAccessToken(string refreshToken);
    
    /// <summary>
    /// Generates a new API key.
    /// </summary>
    (string keyId, string apiKey) GenerateApiKey(string clientId, string name, string[] permissions);
    
    /// <summary>
    /// Validates an API key.
    /// </summary>
    TokenValidationResult ValidateApiKey(string apiKey);
    
    /// <summary>
    /// Revokes an API key.
    /// </summary>
    bool RevokeApiKey(string keyId, string reason);
    
    /// <summary>
    /// Revokes all tokens for a client.
    /// </summary>
    void RevokeAllClientTokens(string clientId);
}

/// <summary>
/// Default implementation of ITokenService.
/// In production, use a proper JWT library like System.IdentityModel.Tokens.Jwt
/// </summary>
public class TokenService : ITokenService
{
    private readonly TokenConfiguration config;
    
    // In production, these would be in a persistent store (Redis, DB)
    private readonly ConcurrentDictionary<string, (string clientId, DateTime expires)> refreshTokens = new();
    private readonly ConcurrentDictionary<string, ApiKey> apiKeys = new();
    private readonly ConcurrentDictionary<string, List<string>> revokedTokens = new();
    private readonly ConcurrentDictionary<string, (int attempts, DateTime? lockoutEnd)> failedAttempts = new();
    
    public TokenService(TokenConfiguration config)
    {
        config = config ?? throw new ArgumentNullException(nameof(config));
        
        if (string.IsNullOrEmpty(config.JwtSecretKey) || config.JwtSecretKey.Length < 32)
        {
            throw new ArgumentException("JWT secret key must be at least 32 characters");
        }
    }
    
    public string GenerateAccessToken(TokenClaims claims)
    {
        // Simplified JWT structure - in production use a proper JWT library
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new
        {
            sub = claims.Subject,
            client_id = claims.ClientId,
            roles = claims.Roles,
            permissions = claims.Permissions,
            custom = claims.CustomClaims,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = DateTimeOffset.UtcNow.Add(config.AccessTokenExpiration).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("N"),
            iss = config.Issuer,
            aud = config.Audience
        };
        
        var headerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header));
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        
        var headerBase64 = Base64UrlEncode(headerBytes);
        var payloadBase64 = Base64UrlEncode(payloadBytes);
        
        var dataToSign = $"{headerBase64}.{payloadBase64}";
        var signature = ComputeHmacSha256(dataToSign, config.JwtSecretKey);
        
        return $"{dataToSign}.{signature}";
    }
    
    public string GenerateRefreshToken(string clientId)
    {
        var token = GenerateSecureToken(64);
        var tokenHash = ComputeSha256(token);
        
        refreshTokens[tokenHash] = (clientId, DateTime.UtcNow.Add(config.RefreshTokenExpiration));
        
        return token;
    }
    
    public TokenValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return TokenValidationResult.Failure("Token is empty");
            
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return TokenValidationResult.Failure("Invalid token format");
                
            var header = parts[0];
            var payload = parts[1];
            var signature = parts[2];
            
            // Verify signature
            var dataToVerify = $"{header}.{payload}";
            var expectedSignature = ComputeHmacSha256(dataToVerify, config.JwtSecretKey);
            
            if (!CryptographicEquals(signature, expectedSignature))
                return TokenValidationResult.Failure("Invalid signature");
                
            // Decode payload
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payload));
            var payloadData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
            
            if (payloadData == null)
                return TokenValidationResult.Failure("Could not decode token payload");
                
            // Check expiration
            if (payloadData.TryGetValue("exp", out var expElement))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                if (exp < DateTimeOffset.UtcNow)
                    return TokenValidationResult.Failure("Token expired");
            }
            
            // Check issuer
            if (payloadData.TryGetValue("iss", out var issElement))
            {
                if (issElement.GetString() != config.Issuer)
                    return TokenValidationResult.Failure("Invalid issuer");
            }
            
            // Check if token is revoked
            if (payloadData.TryGetValue("jti", out var jtiElement))
            {
                var jti = jtiElement.GetString() ?? string.Empty;
                if (payloadData.TryGetValue("client_id", out var clientIdElement))
                {
                    var clientId = clientIdElement.GetString() ?? string.Empty;
                    if (revokedTokens.TryGetValue(clientId, out var revokedList) && 
                        revokedList.Contains(jti))
                    {
                        return TokenValidationResult.Failure("Token has been revoked");
                    }
                }
            }
            
            // Build claims
            var claims = new TokenClaims
            {
                Subject = payloadData.GetValueOrDefault("sub").GetString() ?? string.Empty,
                ClientId = payloadData.GetValueOrDefault("client_id").GetString() ?? string.Empty,
                Roles = GetStringArray(payloadData, "roles"),
                Permissions = GetStringArray(payloadData, "permissions"),
                IssuedAt = DateTimeOffset.FromUnixTimeSeconds(
                    payloadData.GetValueOrDefault("iat").GetInt64()).DateTime,
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(
                    payloadData.GetValueOrDefault("exp").GetInt64()).DateTime,
                TokenId = payloadData.GetValueOrDefault("jti").GetString() ?? string.Empty
            };
            
            return TokenValidationResult.Success(claims);
        }
        catch (Exception ex)
        {
            return TokenValidationResult.Failure($"Token validation failed: {ex.Message}");
        }
    }
    
    public (string accessToken, string refreshToken)? RefreshAccessToken(string refreshToken)
    {
        var tokenHash = ComputeSha256(refreshToken);
        
        if (!refreshTokens.TryGetValue(tokenHash, out var data))
            return null;
            
        if (data.expires < DateTime.UtcNow)
        {
            refreshTokens.TryRemove(tokenHash, out _);
            return null;
        }
        
        // Remove old refresh token (single use)
        refreshTokens.TryRemove(tokenHash, out _);
        
        // Generate new tokens
        var claims = new TokenClaims
        {
            ClientId = data.clientId,
            Subject = data.clientId
        };
        
        var newAccessToken = GenerateAccessToken(claims);
        var newRefreshToken = GenerateRefreshToken(data.clientId);
        
        return (newAccessToken, newRefreshToken);
    }
    
    public (string keyId, string apiKey) GenerateApiKey(string clientId, string name, string[] permissions)
    {
        var keyId = $"ipk_{GenerateSecureToken(8)}";
        var apiKey = $"ips_{GenerateSecureToken(32)}";
        var hashedKey = ComputeSha256(apiKey);
        
        var key = new ApiKey
        {
            KeyId = keyId,
            HashedKey = hashedKey,
            ClientId = clientId,
            Name = InputSanitizer.SanitizeString(name, 100),
            Permissions = permissions,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = config.ApiKeyExpiration.HasValue 
                ? DateTime.UtcNow.Add(config.ApiKeyExpiration.Value) 
                : null
        };
        
        apiKeys[hashedKey] = key;
        
        return (keyId, apiKey);
    }
    
    public TokenValidationResult ValidateApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return TokenValidationResult.Failure("API key is empty");
            
        var hashedKey = ComputeSha256(apiKey);
        
        if (!apiKeys.TryGetValue(hashedKey, out var key))
            return TokenValidationResult.Failure("Invalid API key");
            
        if (key.IsRevoked)
            return TokenValidationResult.Failure($"API key revoked: {key.RevokedReason}");
            
        if (key.ExpiresAt.HasValue && key.ExpiresAt < DateTime.UtcNow)
            return TokenValidationResult.Failure("API key expired");
            
        // Update last used
        key.LastUsedAt = DateTime.UtcNow;
        
        var claims = new TokenClaims
        {
            ClientId = key.ClientId,
            Subject = key.KeyId,
            Permissions = key.Permissions,
            TokenId = key.KeyId
        };
        
        return TokenValidationResult.Success(claims);
    }
    
    public bool RevokeApiKey(string keyId, string reason)
    {
        var key = apiKeys.Values.FirstOrDefault(k => k.KeyId == keyId);
        if (key == null)
            return false;
            
        key.IsRevoked = true;
        key.RevokedReason = reason;
        
        return true;
    }
    
    public void RevokeAllClientTokens(string clientId)
    {
        // Remove all refresh tokens for client
        var toRemove = refreshTokens
            .Where(kvp => kvp.Value.clientId == clientId)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in toRemove)
        {
            refreshTokens.TryRemove(key, out _);
        }
        
        // Revoke all API keys for client
        var apiKeysToRevoke = apiKeys.Values
            .Where(k => k.ClientId == clientId)
            .ToList();
            
        foreach (var key in apiKeysToRevoke)
        {
            key.IsRevoked = true;
            key.RevokedReason = "All client tokens revoked";
        }
    }
    
    // Helper methods
    
    private static string GenerateSecureToken(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
    
    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }
    
    private static string ComputeSha256(string data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
    
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
    
    private static byte[] Base64UrlDecode(string data)
    {
        var base64 = data.Replace("-", "+").Replace("_", "/");
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
    
    private static bool CryptographicEquals(string a, string b)
    {
        // Constant-time comparison to prevent timing attacks
        if (a.Length != b.Length)
            return false;
            
        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
    
    private static string[] GetStringArray(Dictionary<string, JsonElement> data, string key)
    {
        if (!data.TryGetValue(key, out var element))
            return Array.Empty<string>();
            
        if (element.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
            
        return element.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
    }
}
