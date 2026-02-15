// ============================================================================
// StockAnalysisSource - Scrapes stockanalysis.com premarket gainers
// ============================================================================
// Based on the Python script provided by the user

using AngleSharp;
using AngleSharp.Html.Parser;

namespace IdiotProof.Web.Services.MarketScanner.Sources;

/// <summary>
/// Scrapes premarket gainers from stockanalysis.com
/// </summary>
public sealed class StockAnalysisSource : GapperSourceBase
{
    private const string URL = "https://stockanalysis.com/markets/premarket/gainers/";
    
    public override string SourceName => "StockAnalysis";
    public override int Priority => 10;  // High priority - reliable source
    public override TimeSpan RefreshInterval => TimeSpan.FromMinutes(1);
    
    public StockAnalysisSource(HttpClient httpClient, ILogger<StockAnalysisSource> logger) 
        : base(httpClient, logger) { }
    
    public override async Task<IReadOnlyList<RawGapperData>> FetchGappersAsync(CancellationToken ct = default)
    {
        var results = new List<RawGapperData>();
        
        try
        {
            var html = await FetchHtmlAsync(URL, ct);
            
            // Parse HTML with AngleSharp
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = context.GetService<IHtmlParser>()!;
            var document = await parser.ParseDocumentAsync(html, ct);
            
            // Find the main table
            var table = document.QuerySelector("table");
            if (table == null)
            {
                _logger.LogWarning("[StockAnalysis] Could not find table element");
                return results;
            }
            
            // Get headers to find column indices
            var headers = table.QuerySelectorAll("thead th")
                .Select(th => th.TextContent.Trim().ToLowerInvariant())
                .ToList();
            
            int symbolIdx = headers.FindIndex(h => h.Contains("symbol") || h.Contains("ticker"));
            int nameIdx = headers.FindIndex(h => h.Contains("name") || h.Contains("company"));
            int priceIdx = headers.FindIndex(h => h.Contains("price") || h.Contains("premkt"));
            int changeIdx = headers.FindIndex(h => h.Contains("change") || h.Contains("%"));
            int volumeIdx = headers.FindIndex(h => h.Contains("volume") || h.Contains("vol"));
            
            if (symbolIdx < 0)
            {
                _logger.LogWarning("[StockAnalysis] Could not find Symbol column. Headers: {Headers}", 
                    string.Join(", ", headers));
                return results;
            }
            
            // Parse rows
            var rows = table.QuerySelectorAll("tbody tr");
            foreach (var row in rows)
            {
                var cells = row.QuerySelectorAll("td").ToList();
                if (cells.Count <= symbolIdx) continue;
                
                var symbol = cells[symbolIdx].TextContent.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(symbol)) continue;
                
                var gapper = new RawGapperData
                {
                    Source = SourceName,
                    Symbol = symbol,
                    CompanyName = nameIdx >= 0 && cells.Count > nameIdx 
                        ? cells[nameIdx].TextContent.Trim() 
                        : null,
                    Price = priceIdx >= 0 && cells.Count > priceIdx 
                        ? ParsePrice(cells[priceIdx].TextContent) 
                        : null,
                    GapPercent = changeIdx >= 0 && cells.Count > changeIdx 
                        ? ParsePercent(cells[changeIdx].TextContent) 
                        : null,
                    Volume = volumeIdx >= 0 && cells.Count > volumeIdx 
                        ? ParseVolume(cells[volumeIdx].TextContent) 
                        : null
                };
                
                // Only include if we have meaningful data
                if (gapper.Price > 0 || gapper.GapPercent.HasValue)
                {
                    results.Add(gapper);
                }
            }
            
            _logger.LogInformation("[StockAnalysis] Fetched {Count} gappers", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StockAnalysis] Error fetching gappers");
            IsHealthy = false;
        }
        
        return results;
    }
}
