// ============================================================================
// TradingViewSource - Fetches premarket gappers from TradingView
// ============================================================================
// Note: TradingView has an API but premarket data is behind paid tiers.
// This scraper targets their public premarket gappers page.

using AngleSharp;
using AngleSharp.Html.Parser;

namespace IdiotProof.Web.Services.MarketScanner.Sources;

/// <summary>
/// Scrapes premarket gappers from TradingView's public pages.
/// </summary>
public sealed class TradingViewSource : GapperSourceBase
{
    private const string URL = "https://www.tradingview.com/markets/stocks-usa/market-movers-pre-market-gappers/";
    
    public override string SourceName => "TradingView";
    public override int Priority => 25;
    public override TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);
    
    public TradingViewSource(HttpClient httpClient, ILogger<TradingViewSource> logger) 
        : base(httpClient, logger) 
    {
        // TradingView needs specific headers
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.tradingview.com/");
    }
    
    public override async Task<IReadOnlyList<RawGapperData>> FetchGappersAsync(CancellationToken ct = default)
    {
        var results = new List<RawGapperData>();
        
        try
        {
            var html = await FetchHtmlAsync(URL, ct);
            
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = context.GetService<IHtmlParser>()!;
            var document = await parser.ParseDocumentAsync(html, ct);
            
            // TradingView uses a specific table structure
            // They may use JavaScript to load data, so this might be limited
            var table = document.QuerySelector("table") 
                     ?? document.QuerySelector("[class*='tableWrap']")
                     ?? document.QuerySelector("[data-name='screener-table']");
            
            if (table == null)
            {
                _logger.LogWarning("[TradingView] Could not find table element - page may require JS");
                return results;
            }
            
            // Parse headers
            var headers = table.QuerySelectorAll("th")
                .Select(th => th.TextContent.Trim().ToLowerInvariant())
                .ToList();
            
            int symbolIdx = headers.FindIndex(h => h.Contains("symbol") || h.Contains("ticker"));
            int priceIdx = headers.FindIndex(h => h.Contains("price") || h.Contains("last"));
            int changeIdx = headers.FindIndex(h => h.Contains("change") || h.Contains("chg"));
            int volumeIdx = headers.FindIndex(h => h.Contains("volume") || h.Contains("vol"));
            int marketCapIdx = headers.FindIndex(h => h.Contains("market cap") || h.Contains("mktcap"));
            
            if (symbolIdx < 0)
            {
                // Try finding rows directly (some TradingView pages use different structure)
                var rows = document.QuerySelectorAll("[class*='row']");
                foreach (var row in rows.Take(50))
                {
                    var symbolEl = row.QuerySelector("[class*='symbol']") ?? row.QuerySelector("a[href*='symbol']");
                    if (symbolEl == null) continue;
                    
                    var symbol = symbolEl.TextContent.Trim().ToUpperInvariant();
                    if (string.IsNullOrEmpty(symbol) || symbol.Length > 6) continue;
                    
                    var changeEl = row.QuerySelector("[class*='change']");
                    var priceEl = row.QuerySelector("[class*='price']") ?? row.QuerySelector("[class*='last']");
                    var volumeEl = row.QuerySelector("[class*='volume']");
                    
                    results.Add(new RawGapperData
                    {
                        Source = SourceName,
                        Symbol = symbol,
                        Price = priceEl != null ? ParsePrice(priceEl.TextContent) : null,
                        GapPercent = changeEl != null ? ParsePercent(changeEl.TextContent) : null,
                        Volume = volumeEl != null ? ParseVolume(volumeEl.TextContent) : null
                    });
                }
            }
            else
            {
                // Standard table parsing
                var rows = table.QuerySelectorAll("tbody tr");
                foreach (var row in rows)
                {
                    var cells = row.QuerySelectorAll("td").ToList();
                    if (cells.Count <= symbolIdx) continue;
                    
                    var symbolCell = cells[symbolIdx];
                    var symbol = symbolCell.QuerySelector("a")?.TextContent.Trim() 
                              ?? symbolCell.TextContent.Trim();
                    symbol = symbol.ToUpperInvariant();
                    
                    if (string.IsNullOrEmpty(symbol)) continue;
                    
                    results.Add(new RawGapperData
                    {
                        Source = SourceName,
                        Symbol = symbol,
                        Price = priceIdx >= 0 && cells.Count > priceIdx 
                            ? ParsePrice(cells[priceIdx].TextContent) 
                            : null,
                        GapPercent = changeIdx >= 0 && cells.Count > changeIdx 
                            ? ParsePercent(cells[changeIdx].TextContent) 
                            : null,
                        Volume = volumeIdx >= 0 && cells.Count > volumeIdx 
                            ? ParseVolume(cells[volumeIdx].TextContent) 
                            : null,
                        MarketCap = marketCapIdx >= 0 && cells.Count > marketCapIdx 
                            ? ParseMarketCap(cells[marketCapIdx].TextContent) 
                            : null
                    });
                }
            }
            
            _logger.LogInformation("[TradingView] Fetched {Count} gappers", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TradingView] Error fetching gappers");
            IsHealthy = false;
        }
        
        return results;
    }
}
