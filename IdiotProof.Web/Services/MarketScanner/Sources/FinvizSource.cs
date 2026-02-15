// ============================================================================
// FinvizSource - Scrapes finviz.com screener for gappers
// ============================================================================

using AngleSharp;
using AngleSharp.Html.Parser;

namespace IdiotProof.Web.Services.MarketScanner.Sources;

/// <summary>
/// Scrapes gapper candidates from finviz.com screener
/// </summary>
public sealed class FinvizSource : GapperSourceBase
{
    // Finviz screener URL for stocks gapping up 3%+ with volume
    private const string SCREENER_URL = "https://finviz.com/screener.ashx?v=111&f=sh_avgvol_o100,ta_gap_u3&ft=4&o=-change";
    
    public override string SourceName => "Finviz";
    public override int Priority => 15;
    public override TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);
    
    public FinvizSource(HttpClient httpClient, ILogger<FinvizSource> logger) 
        : base(httpClient, logger) 
    {
        // Finviz needs a referer
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://finviz.com/");
    }
    
    public override async Task<IReadOnlyList<RawGapperData>> FetchGappersAsync(CancellationToken ct = default)
    {
        var results = new List<RawGapperData>();
        
        try
        {
            var html = await FetchHtmlAsync(SCREENER_URL, ct);
            
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = context.GetService<IHtmlParser>()!;
            var document = await parser.ParseDocumentAsync(html, ct);
            
            // Finviz uses a specific table structure
            var table = document.QuerySelector("table.screener_table") 
                     ?? document.QuerySelector("#screener-content table");
            
            if (table == null)
            {
                // Try finding by class patterns
                var tables = document.QuerySelectorAll("table");
                table = tables.FirstOrDefault(t => 
                    t.QuerySelectorAll("tr").Count() > 5 &&
                    t.TextContent.Contains("Ticker"));
                
                if (table == null)
                {
                    _logger.LogWarning("[Finviz] Could not find screener table");
                    return results;
                }
            }
            
            // Parse headers
            var headerRow = table.QuerySelector("tr");
            if (headerRow == null) return results;
            
            var headers = headerRow.QuerySelectorAll("td, th")
                .Select(c => c.TextContent.Trim().ToLowerInvariant())
                .ToList();
            
            int tickerIdx = headers.FindIndex(h => h.Contains("ticker"));
            int companyIdx = headers.FindIndex(h => h.Contains("company"));
            int sectorIdx = headers.FindIndex(h => h.Contains("sector"));
            int marketCapIdx = headers.FindIndex(h => h.Contains("market cap") || h.Contains("mktcap"));
            int priceIdx = headers.FindIndex(h => h.Contains("price"));
            int changeIdx = headers.FindIndex(h => h.Contains("change") && !h.Contains("volume"));
            int volumeIdx = headers.FindIndex(h => h.Contains("volume"));
            
            if (tickerIdx < 0)
            {
                _logger.LogWarning("[Finviz] Could not find Ticker column. Headers: {Headers}", 
                    string.Join(", ", headers));
                return results;
            }
            
            // Parse data rows (skip header)
            var rows = table.QuerySelectorAll("tr").Skip(1);
            foreach (var row in rows)
            {
                var cells = row.QuerySelectorAll("td").ToList();
                if (cells.Count <= tickerIdx) continue;
                
                var symbol = cells[tickerIdx].TextContent.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(symbol)) continue;
                
                results.Add(new RawGapperData
                {
                    Source = SourceName,
                    Symbol = symbol,
                    CompanyName = companyIdx >= 0 && cells.Count > companyIdx 
                        ? cells[companyIdx].TextContent.Trim() 
                        : null,
                    Sector = sectorIdx >= 0 && cells.Count > sectorIdx 
                        ? cells[sectorIdx].TextContent.Trim() 
                        : null,
                    MarketCap = marketCapIdx >= 0 && cells.Count > marketCapIdx 
                        ? ParseMarketCap(cells[marketCapIdx].TextContent) 
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
                });
            }
            
            _logger.LogInformation("[Finviz] Fetched {Count} gappers", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Finviz] Error fetching gappers");
            IsHealthy = false;
        }
        
        return results;
    }
}
