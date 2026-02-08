// ============================================================================
// SentimentService - News and Market Sentiment Analysis
// ============================================================================
//
// PURPOSE:
// Fetches news articles, earnings reports, social sentiment, and SEC filings
// from external APIs to provide fundamental context for trading decisions.
//
// DATA SOURCES:
// - Finnhub (free tier: 60 calls/min) - News, earnings, insider transactions
// - Alpha Vantage - News sentiment with scores (free tier: 25 calls/day)
// - SEC EDGAR (free) - 8-K filings for material events
// - Polygon.io (optional) - Additional news and ticker details
//
// USAGE:
// The sentiment score (-100 to +100) is factored into the market score
// calculation, adjusting technical signals based on fundamental context.
//
// ============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdiotProof.Services {
    /// <summary>
    /// Service for fetching and analyzing market sentiment from news, earnings, and social data.
    /// Integrates multiple data sources to build a comprehensive sentiment picture.
    /// </summary>
    public sealed class SentimentService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient _alphaVantageClient;
        private readonly HttpClient _polygonClient;
        private readonly string _finnhubApiKey;
        private readonly string _alphaVantageApiKey;
        private readonly string _polygonApiKey;
        private readonly Dictionary<string, SentimentCache> _cache = new();
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(15);
        private readonly SemaphoreSlim _rateLimiter = new(1, 1);
        private DateTime _lastApiCall = DateTime.MinValue;
        private readonly TimeSpan _minCallInterval = TimeSpan.FromSeconds(1.1); // 60 calls/min = ~1 call/sec
        
        // Track API call counts for rate limiting
        private int _alphaVantageCallsToday = 0;
        private DateOnly _alphaVantageCallDate = DateOnly.MinValue;
        private const int MaxAlphaVantageDailyCalls = 25;

        // Extended sentiment keywords for NLP scoring
        private static readonly Dictionary<string, int> BullishKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Earnings-related
            { "beat", 20 }, { "beats", 20 }, { "exceeds", 20 }, { "exceeded", 20 },
            { "outperform", 20 }, { "outperforms", 20 }, { "top", 10 }, { "tops", 10 },
            { "surpass", 20 }, { "surpasses", 20 },
            
            // Price action
            { "surge", 25 }, { "surges", 25 }, { "soar", 25 }, { "soars", 25 },
            { "rally", 20 }, { "rallies", 20 }, { "breakout", 15 }, { "moonshot", 30 },
            { "rocket", 25 }, { "skyrocket", 30 }, { "explode", 25 },
            
            // Analyst actions
            { "upgrade", 25 }, { "upgrades", 25 }, { "upgraded", 25 },
            { "buy rating", 30 }, { "overweight", 20 }, { "strong buy", 35 },
            
            // Fundamental strength
            { "bullish", 20 }, { "positive", 10 }, { "growth", 15 }, { "growing", 15 },
            { "record", 15 }, { "profit", 10 }, { "gains", 10 }, { "revenue growth", 20 },
            { "strong", 10 }, { "robust", 15 }, { "resilient", 15 },
            
            // Corporate actions (positive)
            { "innovation", 15 }, { "breakthrough", 20 }, { "partnership", 10 },
            { "expansion", 15 }, { "dividend", 15 }, { "buyback", 20 },
            { "acquisition", 15 }, { "merger", 15 }, { "strategic", 10 },
            
            // Regulatory
            { "approval", 20 }, { "approved", 20 }, { "FDA approval", 30 },
            { "clearance", 20 }, { "patent", 15 }, { "licensed", 15 },
            
            // Social/Momentum
            { "squeeze", 25 }, { "short squeeze", 30 }, { "gamma", 20 },
            { "trending", 15 }, { "viral", 15 }, { "catalyst", 20 }
        };

        private static readonly Dictionary<string, int> BearishKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Earnings-related
            { "miss", -20 }, { "misses", -20 }, { "missed", -20 },
            { "disappointing", -20 }, { "disappoints", -20 }, { "shortfall", -20 },
            { "underperform", -20 }, { "below expectations", -25 },
            
            // Price action
            { "decline", -15 }, { "declines", -15 }, { "drop", -15 }, { "drops", -15 },
            { "crash", -30 }, { "crashes", -30 }, { "plunge", -25 }, { "plunges", -25 },
            { "tank", -25 }, { "tanks", -25 }, { "collapse", -30 }, { "tumble", -20 },
            { "selloff", -25 }, { "sell-off", -25 },
            
            // Analyst actions
            { "downgrade", -25 }, { "downgrades", -25 }, { "downgraded", -25 },
            { "sell rating", -30 }, { "underweight", -20 }, { "strong sell", -35 },
            
            // Fundamental weakness
            { "bearish", -20 }, { "negative", -10 }, { "loss", -15 }, { "losses", -15 },
            { "sell", -15 }, { "weak", -10 }, { "weakness", -15 },
            { "slowing", -15 }, { "slowdown", -20 },
            
            // Corporate actions (negative)
            { "lawsuit", -20 }, { "investigation", -20 }, { "fraud", -30 },
            { "layoff", -20 }, { "layoffs", -20 }, { "restructuring", -15 },
            { "bankruptcy", -40 }, { "default", -35 }, { "insolvency", -35 },
            { "delisting", -35 }, { "dilution", -25 },
            
            // Economic
            { "recession", -25 }, { "inflation", -15 }, { "stagflation", -25 },
            
            // Regulatory
            { "warning", -15 }, { "guidance cut", -25 }, { "cut guidance", -25 },
            { "recall", -25 }, { "rejected", -20 }, { "FDA rejection", -30 },
            { "subpoena", -25 }, { "indictment", -30 },
            
            // Risk
            { "risk", -10 }, { "concern", -10 }, { "uncertainty", -15 },
            { "volatile", -10 }, { "headwind", -15 }
        };

        public SentimentService(string? finnhubApiKey = null, string? alphaVantageApiKey = null, string? polygonApiKey = null)
        {
            _finnhubApiKey = finnhubApiKey ?? Environment.GetEnvironmentVariable("FINNHUB_API_KEY") ?? "";
            _alphaVantageApiKey = alphaVantageApiKey ?? Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY") ?? "";
            _polygonApiKey = polygonApiKey ?? Environment.GetEnvironmentVariable("POLYGON_API_KEY") ?? "";
            
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://finnhub.io/api/v1/"),
                Timeout = TimeSpan.FromSeconds(10)
            };
            
            _alphaVantageClient = new HttpClient
            {
                BaseAddress = new Uri("https://www.alphavantage.co/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
            
            _polygonClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.polygon.io/"),
                Timeout = TimeSpan.FromSeconds(10)
            };

            LogApiKeyStatus();
        }
        
        private void LogApiKeyStatus()
        {
            Console.WriteLine("[SENTIMENT] API Key Status:");
            Console.WriteLine($"  - Finnhub:       {(string.IsNullOrEmpty(_finnhubApiKey) ? "NOT SET" : "Configured")}");
            Console.WriteLine($"  - Alpha Vantage: {(string.IsNullOrEmpty(_alphaVantageApiKey) ? "NOT SET" : "Configured")}");
            Console.WriteLine($"  - Polygon.io:    {(string.IsNullOrEmpty(_polygonApiKey) ? "NOT SET" : "Configured")}");
            
            if (string.IsNullOrEmpty(_finnhubApiKey))
            {
                Console.WriteLine("            Get a free Finnhub API key at: https://finnhub.io/register");
            }
            if (string.IsNullOrEmpty(_alphaVantageApiKey))
            {
                Console.WriteLine("            Get a free Alpha Vantage API key at: https://www.alphavantage.co/support/#api-key");
            }
            if (string.IsNullOrEmpty(_polygonApiKey))
            {
                Console.WriteLine("            Get a free Polygon.io API key at: https://polygon.io/dashboard/signup");
            }
        }

        /// <summary>
        /// Gets the overall sentiment score for a symbol.
        /// Returns a score from -100 (very bearish) to +100 (very bullish).
        /// </summary>
        public async Task<SentimentResult> GetSentimentAsync(string symbol)
        {
            // Check cache first
            if (_cache.TryGetValue(symbol, out var cached) && !cached.IsExpired(_cacheExpiry))
            {
                return cached.Result;
            }

            if (string.IsNullOrEmpty(_finnhubApiKey))
            {
                return new SentimentResult { Symbol = symbol, Score = 0, Confidence = 0, Message = "No API key configured" };
            }

            try
            {
                await _rateLimiter.WaitAsync();
                try
                {
                    // Respect rate limits
                    var timeSinceLastCall = DateTime.UtcNow - _lastApiCall;
                    if (timeSinceLastCall < _minCallInterval)
                    {
                        await Task.Delay(_minCallInterval - timeSinceLastCall);
                    }

                    var result = await FetchSentimentAsync(symbol);
                    _cache[symbol] = new SentimentCache { Result = result, FetchedAt = DateTime.UtcNow };
                    _lastApiCall = DateTime.UtcNow;
                    return result;
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SENTIMENT] Error fetching sentiment for {symbol}: {ex.Message}");
                return new SentimentResult { Symbol = symbol, Score = 0, Confidence = 0, Message = ex.Message };
            }
        }

        private async Task<SentimentResult> FetchSentimentAsync(string symbol)
        {
            var result = new SentimentResult { Symbol = symbol };
            var scores = new List<int>();

            // 1. Fetch company news
            var newsScore = await FetchNewsScoreAsync(symbol);
            if (newsScore.HasValue)
            {
                scores.Add(newsScore.Value);
                result.NewsScore = newsScore.Value;
                result.NewsCount = result.NewsArticles.Count;
            }

            // 2. Fetch recommendation trends
            var recoScore = await FetchRecommendationScoreAsync(symbol);
            if (recoScore.HasValue)
            {
                scores.Add(recoScore.Value);
                result.AnalystScore = recoScore.Value;
            }

            // 3. Fetch earnings surprises
            var earningsScore = await FetchEarningsSurpriseScoreAsync(symbol);
            if (earningsScore.HasValue)
            {
                scores.Add(earningsScore.Value);
                result.EarningsScore = earningsScore.Value;
            }

            // 4. Fetch insider transactions
            var insiderScore = await FetchInsiderScoreAsync(symbol);
            if (insiderScore.HasValue)
            {
                scores.Add(insiderScore.Value);
                result.InsiderScore = insiderScore.Value;
            }
            
            // 5. Fetch Alpha Vantage news sentiment (if API key available)
            if (!string.IsNullOrEmpty(_alphaVantageApiKey))
            {
                var avScore = await FetchAlphaVantageSentimentAsync(symbol);
                if (avScore.HasValue)
                {
                    scores.Add(avScore.Value);
                    result.AlphaVantageSentiment = avScore.Value;
                }
            }
            
            // 6. Fetch Polygon.io ticker news (if API key available)
            if (!string.IsNullOrEmpty(_polygonApiKey))
            {
                var polygonScore = await FetchPolygonNewsAsync(symbol);
                if (polygonScore.HasValue)
                {
                    scores.Add(polygonScore.Value);
                    result.PolygonSentiment = polygonScore.Value;
                }
            }

            // Calculate weighted average with additional sources
            if (scores.Count > 0)
            {
                // Weights adjusted based on available sources
                double finnhubWeight = 0.70; // Base Finnhub sources
                double avWeight = !string.IsNullOrEmpty(_alphaVantageApiKey) ? 0.15 : 0;
                double polygonWeight = !string.IsNullOrEmpty(_polygonApiKey) ? 0.15 : 0;
                
                // Normalize if some sources unavailable
                double totalWeight = finnhubWeight + avWeight + polygonWeight;
                
                double weightedSum = 
                    (result.NewsScore ?? 0) * (0.30 * finnhubWeight / totalWeight) +
                    (result.AnalystScore ?? 0) * (0.30 * finnhubWeight / totalWeight) +
                    (result.EarningsScore ?? 0) * (0.25 * finnhubWeight / totalWeight) +
                    (result.InsiderScore ?? 0) * (0.15 * finnhubWeight / totalWeight) +
                    (result.AlphaVantageSentiment ?? 0) * (avWeight / totalWeight) +
                    (result.PolygonSentiment ?? 0) * (polygonWeight / totalWeight);

                result.Score = (int)Math.Clamp(weightedSum, -100, 100);
                result.Confidence = Math.Min(scores.Count * 17, 100); // ~17% per source, max 6 sources
            }

            return result;
        }
        
        /// <summary>
        /// Fetches sentiment from Alpha Vantage News Sentiment API.
        /// Includes pre-computed sentiment scores from their ML model.
        /// </summary>
        private async Task<int?> FetchAlphaVantageSentimentAsync(string symbol)
        {
            // Check daily rate limit
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (_alphaVantageCallDate != today)
            {
                _alphaVantageCallDate = today;
                _alphaVantageCallsToday = 0;
            }
            
            if (_alphaVantageCallsToday >= MaxAlphaVantageDailyCalls)
                return null;
                
            try
            {
                var url = $"query?function=NEWS_SENTIMENT&tickers={symbol}&apikey={_alphaVantageApiKey}";
                var response = await _alphaVantageClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return null;
                    
                _alphaVantageCallsToday++;
                
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("feed", out var feed))
                    return null;
                    
                var sentimentScores = new List<double>();
                
                foreach (var article in feed.EnumerateArray().Take(20))
                {
                    // Get ticker-specific sentiment
                    if (article.TryGetProperty("ticker_sentiment", out var tickerSentiment))
                    {
                        foreach (var ts in tickerSentiment.EnumerateArray())
                        {
                            if (ts.TryGetProperty("ticker", out var ticker) &&
                                ticker.GetString()?.Equals(symbol, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                if (ts.TryGetProperty("ticker_sentiment_score", out var score))
                                {
                                    // Score is -1 to 1, convert to -100 to 100
                                    sentimentScores.Add(score.GetDouble() * 100);
                                }
                            }
                        }
                    }
                }
                
                if (sentimentScores.Count == 0)
                    return null;
                    
                // Time-weighted average (recent articles count more)
                double weightedSum = 0;
                double totalWeight = 0;
                for (int i = 0; i < sentimentScores.Count; i++)
                {
                    double weight = Math.Exp(-i * 0.15);
                    weightedSum += sentimentScores[i] * weight;
                    totalWeight += weight;
                }
                
                return (int)(weightedSum / totalWeight);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SENTIMENT] Alpha Vantage error for {symbol}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Fetches news from Polygon.io and scores it.
        /// </summary>
        private async Task<int?> FetchPolygonNewsAsync(string symbol)
        {
            try
            {
                var url = $"v2/reference/news?ticker={symbol}&limit=20&apiKey={_polygonApiKey}";
                var response = await _polygonClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return null;
                    
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("results", out var results))
                    return null;
                    
                var articleScores = new List<int>();
                
                foreach (var article in results.EnumerateArray())
                {
                    string title = article.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string description = article.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    
                    var text = $"{title} {description}";
                    var score = CalculateTextSentiment(text);
                    articleScores.Add(score);
                }
                
                if (articleScores.Count == 0)
                    return null;
                    
                // Time-weighted average
                double weightedSum = 0;
                double totalWeight = 0;
                for (int i = 0; i < articleScores.Count; i++)
                {
                    double weight = Math.Exp(-i * 0.1);
                    weightedSum += articleScores[i] * weight;
                    totalWeight += weight;
                }
                
                return (int)(weightedSum / totalWeight);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SENTIMENT] Polygon error for {symbol}: {ex.Message}");
                return null;
            }
        }

        private async Task<int?> FetchNewsScoreAsync(string symbol)
        {
            try
            {
                var fromDate = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
                var toDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var url = $"company-news?symbol={symbol}&from={fromDate}&to={toDate}&token={_finnhubApiKey}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var news = await response.Content.ReadFromJsonAsync<List<FinnhubNewsItem>>();
                if (news == null || news.Count == 0)
                    return null;

                // Score each article based on keywords
                var articleScores = new List<int>();
                foreach (var article in news.Take(20)) // Analyze last 20 articles
                {
                    var text = $"{article.Headline} {article.Summary}";
                    var score = CalculateTextSentiment(text);
                    articleScores.Add(score);
                }

                // Time-weighted average (recent articles count more)
                if (articleScores.Count == 0) return null;

                double weightedSum = 0;
                double totalWeight = 0;
                for (int i = 0; i < articleScores.Count; i++)
                {
                    // Exponential decay: most recent = weight 1.0, older articles decay
                    double weight = Math.Exp(-i * 0.1);
                    weightedSum += articleScores[i] * weight;
                    totalWeight += weight;
                }

                return (int)(weightedSum / totalWeight);
            }
            catch
            {
                return null;
            }
        }

        private async Task<int?> FetchRecommendationScoreAsync(string symbol)
        {
            try
            {
                var url = $"stock/recommendation?symbol={symbol}&token={_finnhubApiKey}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var recommendations = await response.Content.ReadFromJsonAsync<List<FinnhubRecommendation>>();
                if (recommendations == null || recommendations.Count == 0)
                    return null;

                // Use most recent recommendation
                var latest = recommendations[0];
                int total = latest.StrongBuy + latest.Buy + latest.Hold + latest.Sell + latest.StrongSell;
                if (total == 0) return null;

                // Score: StrongBuy=+100, Buy=+50, Hold=0, Sell=-50, StrongSell=-100
                double score = (
                    latest.StrongBuy * 100 +
                    latest.Buy * 50 +
                    latest.Hold * 0 +
                    latest.Sell * -50 +
                    latest.StrongSell * -100
                ) / (double)total;

                return (int)Math.Clamp(score, -100, 100);
            }
            catch
            {
                return null;
            }
        }

        private async Task<int?> FetchEarningsSurpriseScoreAsync(string symbol)
        {
            try
            {
                var url = $"stock/earnings?symbol={symbol}&token={_finnhubApiKey}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var earnings = await response.Content.ReadFromJsonAsync<List<FinnhubEarnings>>();
                if (earnings == null || earnings.Count == 0)
                    return null;

                // Use last 4 quarters, weighted by recency
                var recentEarnings = earnings.Take(4).ToList();
                if (recentEarnings.Count == 0) return null;

                double weightedSurprise = 0;
                double totalWeight = 0;
                for (int i = 0; i < recentEarnings.Count; i++)
                {
                    var e = recentEarnings[i];
                    if (e.Estimate == 0) continue;

                    // Calculate surprise percentage
                    double surprise = (e.Actual - e.Estimate) / Math.Abs(e.Estimate) * 100;
                    double weight = Math.Exp(-i * 0.5); // Most recent = highest weight

                    weightedSurprise += surprise * weight;
                    totalWeight += weight;
                }

                if (totalWeight == 0) return null;

                // Convert surprise % to score (-100 to +100)
                // A 10% beat = +100, 10% miss = -100
                double avgSurprise = weightedSurprise / totalWeight;
                return (int)Math.Clamp(avgSurprise * 10, -100, 100);
            }
            catch
            {
                return null;
            }
        }

        private async Task<int?> FetchInsiderScoreAsync(string symbol)
        {
            try
            {
                var fromDate = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd");
                var url = $"stock/insider-transactions?symbol={symbol}&from={fromDate}&token={_finnhubApiKey}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var transactions = await response.Content.ReadFromJsonAsync<FinnhubInsiderResponse>();
                if (transactions?.Data == null || transactions.Data.Count == 0)
                    return null;

                // Sum up buy vs sell value
                double buyValue = 0;
                double sellValue = 0;

                foreach (var tx in transactions.Data.Take(50))
                {
                    double value = Math.Abs(tx.Share * tx.Price);
                    if (tx.TransactionType == "P" || tx.TransactionType == "A") // Purchase or Award
                        buyValue += value;
                    else if (tx.TransactionType == "S") // Sale
                        sellValue += value;
                }

                double totalValue = buyValue + sellValue;
                if (totalValue == 0) return null;

                // Net buy ratio: -100 (all sells) to +100 (all buys)
                double netBuyRatio = (buyValue - sellValue) / totalValue * 100;
                return (int)Math.Clamp(netBuyRatio, -100, 100);
            }
            catch
            {
                return null;
            }
        }

        private static int CalculateTextSentiment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int score = 0;
            var words = text.Split([' ', ',', '.', '!', '?', ':', ';', '-', '"', '\''], StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                if (BullishKeywords.TryGetValue(word, out int bullishScore))
                    score += bullishScore;
                if (BearishKeywords.TryGetValue(word, out int bearishScore))
                    score += bearishScore;
            }

            // Check for multi-word phrases
            var lowerText = text.ToLowerInvariant();
            foreach (var kvp in BullishKeywords.Where(k => k.Key.Contains(' ')))
            {
                if (lowerText.Contains(kvp.Key.ToLowerInvariant()))
                    score += kvp.Value;
            }
            foreach (var kvp in BearishKeywords.Where(k => k.Key.Contains(' ')))
            {
                if (lowerText.Contains(kvp.Key.ToLowerInvariant()))
                    score += kvp.Value;
            }

            // Normalize to -100 to +100 range
            return (int)Math.Clamp(score, -100, 100);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _alphaVantageClient.Dispose();
            _polygonClient.Dispose();
            _rateLimiter.Dispose();
        }

        /// <summary>
        /// Invalidates the cache for a specific symbol.
        /// </summary>
        public void InvalidateCache(string symbol)
        {
            _cache.Remove(symbol);
        }

        /// <summary>
        /// Clears the entire sentiment cache.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }
        
        /// <summary>
        /// Gets the number of API calls remaining for Alpha Vantage today.
        /// </summary>
        public int GetAlphaVantageCallsRemaining()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (_alphaVantageCallDate != today)
                return MaxAlphaVantageDailyCalls;
                
            return Math.Max(0, MaxAlphaVantageDailyCalls - _alphaVantageCallsToday);
        }
    }

    // ========================================================================
    // Data Transfer Objects
    // ========================================================================

    public class SentimentResult
    {
        public string Symbol { get; set; } = "";
        public int Score { get; set; }
        public int Confidence { get; set; } // 0-100
        
        // Finnhub sources
        public int? NewsScore { get; set; }
        public int? AnalystScore { get; set; }
        public int? EarningsScore { get; set; }
        public int? InsiderScore { get; set; }
        
        // Additional sources
        public int? AlphaVantageSentiment { get; set; }
        public int? PolygonSentiment { get; set; }
        
        public int NewsCount { get; set; }
        public string? Message { get; set; }
        public List<string> NewsArticles { get; set; } = new();
        
        /// <summary>
        /// Number of data sources that contributed to the score.
        /// </summary>
        public int SourceCount => 
            (NewsScore.HasValue ? 1 : 0) +
            (AnalystScore.HasValue ? 1 : 0) +
            (EarningsScore.HasValue ? 1 : 0) +
            (InsiderScore.HasValue ? 1 : 0) +
            (AlphaVantageSentiment.HasValue ? 1 : 0) +
            (PolygonSentiment.HasValue ? 1 : 0);

        /// <summary>
        /// Gets a score contribution for the market score calculation.
        /// Weighted by confidence level.
        /// </summary>
        public int GetWeightedScore()
        {
            return (int)(Score * (Confidence / 100.0));
        }

        public override string ToString()
        {
            var arrow = Score > 0 ? "+" : "";
            var sources = new List<string>();
            if (NewsScore.HasValue) sources.Add($"News={NewsScore}");
            if (AnalystScore.HasValue) sources.Add($"Analyst={AnalystScore}");
            if (EarningsScore.HasValue) sources.Add($"Earnings={EarningsScore}");
            if (InsiderScore.HasValue) sources.Add($"Insider={InsiderScore}");
            if (AlphaVantageSentiment.HasValue) sources.Add($"AV={AlphaVantageSentiment}");
            if (PolygonSentiment.HasValue) sources.Add($"Polygon={PolygonSentiment}");
            
            return $"[SENTIMENT {Symbol}] {arrow}{Score} (conf={Confidence}%, sources={SourceCount}) {string.Join(", ", sources)}";
        }
    }

    internal class SentimentCache
    {
        public SentimentResult Result { get; set; } = new();
        public DateTime FetchedAt { get; set; }

        public bool IsExpired(TimeSpan expiry) => DateTime.UtcNow - FetchedAt > expiry;
    }

    // Finnhub API response models
    internal class FinnhubNewsItem
    {
        [JsonPropertyName("headline")]
        public string Headline { get; set; } = "";

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";

        [JsonPropertyName("datetime")]
        public long Datetime { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";
    }

    internal class FinnhubRecommendation
    {
        [JsonPropertyName("strongBuy")]
        public int StrongBuy { get; set; }

        [JsonPropertyName("buy")]
        public int Buy { get; set; }

        [JsonPropertyName("hold")]
        public int Hold { get; set; }

        [JsonPropertyName("sell")]
        public int Sell { get; set; }

        [JsonPropertyName("strongSell")]
        public int StrongSell { get; set; }

        [JsonPropertyName("period")]
        public string Period { get; set; } = "";
    }

    internal class FinnhubEarnings
    {
        [JsonPropertyName("actual")]
        public double Actual { get; set; }

        [JsonPropertyName("estimate")]
        public double Estimate { get; set; }

        [JsonPropertyName("period")]
        public string Period { get; set; } = "";
    }

    internal class FinnhubInsiderResponse
    {
        [JsonPropertyName("data")]
        public List<FinnhubInsiderTransaction> Data { get; set; } = new();
    }

    internal class FinnhubInsiderTransaction
    {
        [JsonPropertyName("share")]
        public double Share { get; set; }

        [JsonPropertyName("transactionPrice")]
        public double Price { get; set; }

        [JsonPropertyName("transactionType")]
        public string TransactionType { get; set; } = "";
    }
}
