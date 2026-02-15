// ============================================================================
// BarchartSource - Scrapes barchart.com gap up stocks
// ============================================================================

using AngleSharp;
using AngleSharp.Html.Parser;

namespace IdiotProof.Web.Services.MarketScanner.Sources;

/// <summary>
/// Scrapes gap up stocks from barchart.com
/// </summary>
public sealed class BarchartSource : GapperSourceBase
{
    private const string GAP_UP_URL = "https://www.barchart.com/stocks/performance/gap/gap-up?orderBy=gapUpPercent&orderDir=desc";
    private const string GAP_DOWN_URL = "https://www.barchart.com/stocks/performance/gap/gap-down?orderBy=gapDownPercent&orderDir=desc";
    
    public override string SourceName => "Barchart";
    public override int Priority => 20;
    public override TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);
    
    public BarchartSource(HttpClient httpClient, ILogger<BarchartSource> logger) 
        : base(httpClient, logger) { }
    
    public override async Task<IReadOnlyList<RawGapperData>> FetchGappersAsync(CancellationToken ct = default)
    {
        var results = new List<RawGapperData>();
        
        // Fetch gap ups
        var gapUps = await FetchFromUrlAsync(GAP_UP_URL, isGapUp: true, ct);
        results.AddRange(gapUps);
        
        // Small delay to be polite
        await Task.Delay(500, ct);
        
        // Fetch gap downs
        var gapDowns = await FetchFromUrlAsync(GAP_DOWN_URL, isGapUp: false, ct);
        results.AddRange(gapDowns);
        
        _logger.LogInformation("[Barchart] Fetched {Count} gappers ({Up} up, {Down} down)", 
            results.Count, gapUps.Count, gapDowns.Count);
        
        return results;
    }
    
    private async Task<List<RawGapperData>> FetchFromUrlAsync(string url, bool isGapUp, CancellationToken ct)
    {
        var results = new List<RawGapperData>();
        
        try
        {
            var html = await FetchHtmlAsync(url, ct);
            
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = context.GetService<IHtmlParser>()!;
            var document = await parser.ParseDocumentAsync(html, ct);
            
            // Barchart uses a data table - find it
            var table = document.QuerySelector("table.bc-table-scrollable-inner") 
                     ?? document.QuerySelector("table");
            
            if (table == null)
            {
                _logger.LogWarning("[Barchart] Could not find table at {Url}", url);
                return results;
            }
            
            // Get headers
            var headers = table.QuerySelectorAll("thead th")
                .Select(th => th.TextContent.Trim().ToLowerInvariant())
                .ToList();
            
            int symbolIdx = headers.FindIndex(h => h.Contains("symbol"));
            int lastIdx = headers.FindIndex(h => h.Contains("last"));
            int gapIdx = headers.FindIndex(h => h.Contains("gap"));
            int volumeIdx = headers.FindIndex(h => h.Contains("volume"));
            
            if (symbolIdx < 0) return results;
            
            var rows = table.QuerySelectorAll("tbody tr");
            foreach (var row in rows)
            {
                var cells = row.QuerySelectorAll("td").ToList();
                if (cells.Count <= symbolIdx) continue;
                
                // Symbol might be in an anchor tag
                var symbolCell = cells[symbolIdx];
                var symbol = symbolCell.QuerySelector("a")?.TextContent.Trim() 
                          ?? symbolCell.TextContent.Trim();
                symbol = symbol.ToUpperInvariant();
                
                if (string.IsNullOrEmpty(symbol)) continue;
                
                var gapPercent = gapIdx >= 0 && cells.Count > gapIdx 
                    ? ParsePercent(cells[gapIdx].TextContent) 
                    : 0;
                
                // For gap downs, the value is already negative from the source
                // But we ensure consistency
                if (!isGapUp && gapPercent > 0)
                    gapPercent = -gapPercent;
                
                results.Add(new RawGapperData
                {
                    Source = SourceName,
                    Symbol = symbol,
                    Price = lastIdx >= 0 && cells.Count > lastIdx 
                        ? ParsePrice(cells[lastIdx].TextContent) 
                        : null,
                    GapPercent = gapPercent,
                    Volume = volumeIdx >= 0 && cells.Count > volumeIdx 
                        ? ParseVolume(cells[volumeIdx].TextContent) 
                        : null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Barchart] Error fetching from {Url}", url);
        }
        
        return results;
    }
}
