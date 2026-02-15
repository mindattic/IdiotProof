// ============================================================================
// IGapperSource - Interface for gapper data sources
// ============================================================================

namespace IdiotProof.Web.Services.MarketScanner;

/// <summary>
/// Interface for a source that provides gapper candidates.
/// </summary>
public interface IGapperSource
{
    /// <summary>
    /// Name of this data source.
    /// </summary>
    string SourceName { get; }
    
    /// <summary>
    /// Priority for rate limiting (lower = fetch first).
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// How often this source should be refreshed.
    /// </summary>
    TimeSpan RefreshInterval { get; }
    
    /// <summary>
    /// Last time this source was successfully fetched.
    /// </summary>
    DateTime? LastFetchUtc { get; }
    
    /// <summary>
    /// Whether this source is currently healthy.
    /// </summary>
    bool IsHealthy { get; }
    
    /// <summary>
    /// Fetches gapper candidates from this source.
    /// </summary>
    Task<IReadOnlyList<RawGapperData>> FetchGappersAsync(CancellationToken ct = default);
}

/// <summary>
/// Base class with common HTTP functionality for scrapers.
/// </summary>
public abstract class GapperSourceBase : IGapperSource
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;
    
    public abstract string SourceName { get; }
    public virtual int Priority => 50;
    public virtual TimeSpan RefreshInterval => TimeSpan.FromMinutes(1);
    public DateTime? LastFetchUtc { get; protected set; }
    public bool IsHealthy { get; protected set; } = true;
    
    protected GapperSourceBase(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Set default headers
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", 
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
    }
    
    public abstract Task<IReadOnlyList<RawGapperData>> FetchGappersAsync(CancellationToken ct = default);
    
    protected async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            IsHealthy = true;
            LastFetchUtc = DateTime.UtcNow;
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Source}] Failed to fetch {Url}", SourceName, url);
            IsHealthy = false;
            throw;
        }
    }
    
    protected static double ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        
        var cleaned = value
            .Replace("%", "")
            .Replace("+", "")
            .Replace(",", "")
            .Trim();
        
        return double.TryParse(cleaned, out var result) ? result : 0;
    }
    
    protected static double ParsePrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        
        var cleaned = value
            .Replace("$", "")
            .Replace(",", "")
            .Trim();
        
        return double.TryParse(cleaned, out var result) ? result : 0;
    }
    
    protected static long ParseVolume(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        
        var cleaned = value.Replace(",", "").Trim().ToUpperInvariant();
        
        double multiplier = 1;
        if (cleaned.EndsWith("K"))
        {
            multiplier = 1_000;
            cleaned = cleaned[..^1];
        }
        else if (cleaned.EndsWith("M"))
        {
            multiplier = 1_000_000;
            cleaned = cleaned[..^1];
        }
        else if (cleaned.EndsWith("B"))
        {
            multiplier = 1_000_000_000;
            cleaned = cleaned[..^1];
        }
        
        if (double.TryParse(cleaned, out var result))
            return (long)(result * multiplier);
        
        return 0;
    }
    
    protected static double ParseMarketCap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        
        var cleaned = value.Replace("$", "").Replace(",", "").Trim().ToUpperInvariant();
        
        double multiplier = 1;
        if (cleaned.EndsWith("K"))
        {
            multiplier = 1_000;
            cleaned = cleaned[..^1];
        }
        else if (cleaned.EndsWith("M"))
        {
            multiplier = 1_000_000;
            cleaned = cleaned[..^1];
        }
        else if (cleaned.EndsWith("B"))
        {
            multiplier = 1_000_000_000;
            cleaned = cleaned[..^1];
        }
        else if (cleaned.EndsWith("T"))
        {
            multiplier = 1_000_000_000_000;
            cleaned = cleaned[..^1];
        }
        
        if (double.TryParse(cleaned, out var result))
            return result * multiplier;
        
        return 0;
    }
}
