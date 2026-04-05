// IdiotProof.Core.FutureState.Security
// Rate Limiter for DDoS protection and fair usage
// Supports multiple algorithms: Fixed Window, Sliding Window, Token Bucket

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IdiotProof.Core.FutureState.Security;

/// <summary>
/// Rate limiting configuration.
/// </summary>
public class RateLimitConfiguration
{
    /// <summary>
    /// Maximum requests per window.
    /// </summary>
    public int RequestsPerWindow { get; set; } = 100;
    
    /// <summary>
    /// Window duration.
    /// </summary>
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// Algorithm to use.
    /// </summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.SlidingWindow;
    
    /// <summary>
    /// Token bucket refill rate (tokens per second).
    /// Only used with TokenBucket algorithm.
    /// </summary>
    public double TokenRefillRate { get; set; } = 10;
    
    /// <summary>
    /// Maximum bucket size for TokenBucket algorithm.
    /// </summary>
    public int MaxBucketSize { get; set; } = 100;
    
    /// <summary>
    /// Whether to include retry-after header in responses.
    /// </summary>
    public bool IncludeRetryAfter { get; set; } = true;
    
    /// <summary>
    /// Enable burst allowance (temporary higher limit).
    /// </summary>
    public bool AllowBurst { get; set; } = false;
    
    /// <summary>
    /// Burst multiplier (e.g., 2.0 = allow 2x normal rate briefly).
    /// </summary>
    public double BurstMultiplier { get; set; } = 2.0;
    
    /// <summary>
    /// Custom limits per endpoint.
    /// </summary>
    public Dictionary<string, (int requests, TimeSpan window)> EndpointLimits { get; set; } = new();
}

public enum RateLimitAlgorithm
{
    /// <summary>
    /// Fixed time window (simple, less accurate at boundaries).
    /// </summary>
    FixedWindow,
    
    /// <summary>
    /// Sliding window (smoother, more accurate).
    /// </summary>
    SlidingWindow,
    
    /// <summary>
    /// Token bucket (allows bursts, smooth refill).
    /// </summary>
    TokenBucket
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }
    
    /// <summary>
    /// Current request count in window.
    /// </summary>
    public int CurrentCount { get; init; }
    
    /// <summary>
    /// Maximum allowed requests.
    /// </summary>
    public int MaxAllowed { get; init; }
    
    /// <summary>
    /// Remaining requests in current window.
    /// </summary>
    public int Remaining => Math.Max(0, MaxAllowed - CurrentCount);
    
    /// <summary>
    /// When the limit resets (for Retry-After header).
    /// </summary>
    public DateTime? ResetAt { get; init; }
    
    /// <summary>
    /// Seconds until reset.
    /// </summary>
    public int? RetryAfterSeconds => ResetAt.HasValue 
        ? (int)Math.Ceiling((ResetAt.Value - DateTime.UtcNow).TotalSeconds)
        : null;
        
    /// <summary>
    /// Reason for denial (if not allowed).
    /// </summary>
    public string? DenialReason { get; init; }
}

/// <summary>
/// Interface for rate limiting.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Checks if a request should be allowed.
    /// </summary>
    RateLimitResult CheckLimit(string clientId, string? endpoint = null);
    
    /// <summary>
    /// Asynchronously checks if a request should be allowed (for distributed scenarios).
    /// </summary>
    Task<RateLimitResult> CheckLimitAsync(string clientId, string? endpoint = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resets the limit for a client (e.g., after successful payment).
    /// </summary>
    void ResetLimit(string clientId);
    
    /// <summary>
    /// Gets current usage statistics for a client.
    /// </summary>
    RateLimitResult GetUsage(string clientId, string? endpoint = null);
}

/// <summary>
/// In-memory rate limiter implementation.
/// For production, consider Redis-backed distributed rate limiting.
/// </summary>
public class RateLimiter : IRateLimiter
{
    private readonly RateLimitConfiguration config;
    
    // Fixed/Sliding window tracking
    private readonly ConcurrentDictionary<string, WindowData> windowData = new();
    
    // Token bucket tracking
    private readonly ConcurrentDictionary<string, TokenBucketData> bucketData = new();
    
    // Cleanup timer
    private readonly Timer cleanupTimer;
    
    public RateLimiter(RateLimitConfiguration config)
    {
        config = config ?? throw new ArgumentNullException(nameof(config));
        
        // Cleanup expired entries every 5 minutes
        cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
    
    public RateLimitResult CheckLimit(string clientId, string? endpoint = null)
    {
        var key = GetKey(clientId, endpoint);
        var (maxRequests, window) = GetLimits(endpoint);
        
        return config.Algorithm switch
        {
            RateLimitAlgorithm.FixedWindow => CheckFixedWindow(key, maxRequests, window),
            RateLimitAlgorithm.SlidingWindow => CheckSlidingWindow(key, maxRequests, window),
            RateLimitAlgorithm.TokenBucket => CheckTokenBucket(key),
            _ => CheckSlidingWindow(key, maxRequests, window)
        };
    }
    
    public Task<RateLimitResult> CheckLimitAsync(string clientId, string? endpoint = null, CancellationToken cancellationToken = default)
    {
        // For in-memory implementation, just wrap synchronous call
        // Distributed implementation would use Redis/other async store
        return Task.FromResult(CheckLimit(clientId, endpoint));
    }
    
    public void ResetLimit(string clientId)
    {
        var keysToRemove = new List<string>();
        
        foreach (var key in windowData.Keys)
        {
            if (key.StartsWith(clientId))
                keysToRemove.Add(key);
        }
        
        foreach (var key in keysToRemove)
        {
            windowData.TryRemove(key, out _);
        }
        
        foreach (var key in bucketData.Keys)
        {
            if (key.StartsWith(clientId))
            {
                if (bucketData.TryGetValue(key, out var bucket))
                {
                    bucket.Tokens = config.MaxBucketSize;
                }
            }
        }
    }
    
    public RateLimitResult GetUsage(string clientId, string? endpoint = null)
    {
        var key = GetKey(clientId, endpoint);
        var (maxRequests, window) = GetLimits(endpoint);
        
        if (config.Algorithm == RateLimitAlgorithm.TokenBucket)
        {
            if (bucketData.TryGetValue(key, out var bucket))
            {
                RefillBucket(bucket);
                return new RateLimitResult
                {
                    IsAllowed = true,
                    CurrentCount = config.MaxBucketSize - (int)bucket.Tokens,
                    MaxAllowed = config.MaxBucketSize
                };
            }
        }
        else
        {
            if (windowData.TryGetValue(key, out var data))
            {
                return new RateLimitResult
                {
                    IsAllowed = true,
                    CurrentCount = data.Count,
                    MaxAllowed = maxRequests,
                    ResetAt = data.WindowStart.Add(window)
                };
            }
        }
        
        return new RateLimitResult
        {
            IsAllowed = true,
            CurrentCount = 0,
            MaxAllowed = maxRequests
        };
    }
    
    // Fixed Window Algorithm
    private RateLimitResult CheckFixedWindow(string key, int maxRequests, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var windowStart = new DateTime(now.Ticks - (now.Ticks % window.Ticks), DateTimeKind.Utc);
        
        var data = windowData.AddOrUpdate(
            key,
            _ => new WindowData { WindowStart = windowStart, Count = 1 },
            (_, existing) =>
            {
                if (existing.WindowStart < windowStart)
                {
                    // New window
                    existing.WindowStart = windowStart;
                    existing.Count = 1;
                }
                else
                {
                    existing.Count++;
                }
                return existing;
            });
        
        var allowed = data.Count <= maxRequests;
        
        return new RateLimitResult
        {
            IsAllowed = allowed,
            CurrentCount = data.Count,
            MaxAllowed = maxRequests,
            ResetAt = windowStart.Add(window),
            DenialReason = allowed ? null : "Rate limit exceeded (fixed window)"
        };
    }
    
    // Sliding Window Algorithm
    private RateLimitResult CheckSlidingWindow(string key, int maxRequests, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.Subtract(window);
        
        var data = windowData.GetOrAdd(key, _ => new WindowData { WindowStart = now });
        
        lock (data)
        {
            // Remove old timestamps
            while (data.Timestamps.Count > 0 && data.Timestamps.Peek() < windowStart)
            {
                data.Timestamps.Dequeue();
            }
            
            var currentCount = data.Timestamps.Count;
            var effectiveMax = config.AllowBurst 
                ? (int)(maxRequests * config.BurstMultiplier)
                : maxRequests;
            
            if (currentCount < effectiveMax)
            {
                data.Timestamps.Enqueue(now);
                data.Count = currentCount + 1;
                
                return new RateLimitResult
                {
                    IsAllowed = true,
                    CurrentCount = data.Count,
                    MaxAllowed = maxRequests,
                    ResetAt = data.Timestamps.Peek().Add(window)
                };
            }
            
            return new RateLimitResult
            {
                IsAllowed = false,
                CurrentCount = currentCount,
                MaxAllowed = maxRequests,
                ResetAt = data.Timestamps.Peek().Add(window),
                DenialReason = "Rate limit exceeded (sliding window)"
            };
        }
    }
    
    // Token Bucket Algorithm
    private RateLimitResult CheckTokenBucket(string key)
    {
        var bucket = bucketData.GetOrAdd(key, _ => new TokenBucketData
        {
            Tokens = config.MaxBucketSize,
            LastRefill = DateTime.UtcNow
        });
        
        lock (bucket)
        {
            RefillBucket(bucket);
            
            if (bucket.Tokens >= 1)
            {
                bucket.Tokens -= 1;
                
                return new RateLimitResult
                {
                    IsAllowed = true,
                    CurrentCount = config.MaxBucketSize - (int)bucket.Tokens,
                    MaxAllowed = config.MaxBucketSize
                };
            }
            
            // Calculate when a token will be available
            var secondsUntilToken = 1.0 / config.TokenRefillRate;
            
            return new RateLimitResult
            {
                IsAllowed = false,
                CurrentCount = config.MaxBucketSize,
                MaxAllowed = config.MaxBucketSize,
                ResetAt = DateTime.UtcNow.AddSeconds(secondsUntilToken),
                DenialReason = "Rate limit exceeded (token bucket empty)"
            };
        }
    }
    
    private void RefillBucket(TokenBucketData bucket)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - bucket.LastRefill).TotalSeconds;
        var tokensToAdd = elapsed * config.TokenRefillRate;
        
        bucket.Tokens = Math.Min(config.MaxBucketSize, bucket.Tokens + tokensToAdd);
        bucket.LastRefill = now;
    }
    
    private string GetKey(string clientId, string? endpoint)
    {
        return endpoint != null ? $"{clientId}:{endpoint}" : clientId;
    }
    
    private (int requests, TimeSpan window) GetLimits(string? endpoint)
    {
        if (endpoint != null && config.EndpointLimits.TryGetValue(endpoint, out var limits))
        {
            return limits;
        }
        
        return (config.RequestsPerWindow, config.WindowDuration);
    }
    
    private void CleanupExpiredEntries(object? state)
    {
        var cutoff = DateTime.UtcNow.Subtract(config.WindowDuration * 2);
        
        var keysToRemove = new List<string>();
        
        foreach (var kvp in windowData)
        {
            if (kvp.Value.WindowStart < cutoff)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            windowData.TryRemove(key, out _);
        }
        
        // Clean up token buckets that are full (no activity)
        foreach (var kvp in bucketData)
        {
            if (kvp.Value.Tokens >= config.MaxBucketSize && 
                kvp.Value.LastRefill < cutoff)
            {
                bucketData.TryRemove(kvp.Key, out _);
            }
        }
    }
    
    private class WindowData
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
        public Queue<DateTime> Timestamps { get; } = new();
    }
    
    private class TokenBucketData
    {
        public double Tokens { get; set; }
        public DateTime LastRefill { get; set; }
    }
}

/// <summary>
/// Rate limit policies for different tiers/operations.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Default policy for general API access.
    /// </summary>
    public static RateLimitConfiguration Default => new()
    {
        RequestsPerWindow = 100,
        WindowDuration = TimeSpan.FromMinutes(1),
        Algorithm = RateLimitAlgorithm.SlidingWindow
    };
    
    /// <summary>
    /// Strict policy for authentication endpoints.
    /// </summary>
    public static RateLimitConfiguration Authentication => new()
    {
        RequestsPerWindow = 10,
        WindowDuration = TimeSpan.FromMinutes(5),
        Algorithm = RateLimitAlgorithm.FixedWindow
    };
    
    /// <summary>
    /// Lenient policy for read-only market data.
    /// </summary>
    public static RateLimitConfiguration MarketData => new()
    {
        RequestsPerWindow = 1000,
        WindowDuration = TimeSpan.FromMinutes(1),
        Algorithm = RateLimitAlgorithm.TokenBucket,
        TokenRefillRate = 20,
        MaxBucketSize = 200
    };
    
    /// <summary>
    /// Strict policy for order placement.
    /// </summary>
    public static RateLimitConfiguration Trading => new()
    {
        RequestsPerWindow = 50,
        WindowDuration = TimeSpan.FromMinutes(1),
        Algorithm = RateLimitAlgorithm.SlidingWindow,
        AllowBurst = true,
        BurstMultiplier = 1.5
    };
    
    /// <summary>
    /// Very strict policy for strategy creation.
    /// </summary>
    public static RateLimitConfiguration StrategyCreation => new()
    {
        RequestsPerWindow = 20,
        WindowDuration = TimeSpan.FromHours(1),
        Algorithm = RateLimitAlgorithm.FixedWindow
    };
}
