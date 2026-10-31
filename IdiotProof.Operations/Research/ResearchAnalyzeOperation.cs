using System.Net;
using System.Text.RegularExpressions;
using IdiotProof.Core.Data;
using IdiotProof.Core.Identity;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Research;

public sealed record ResearchAnalyzeArgs
{
    [Argument(0, Name = "target", Description = "A ticker (analyzes its recent Alpaca news, or --text) or an http(s) URL of an article or filing.")]
    public string Target { get; init; } = "";

    [Option("ticker", Description = "The company a URL is about (required with a URL).")]
    public string? Ticker { get; init; }

    [Option("text", Description = "Analyze this text instead of fetching anything.")]
    public string? Text { get; init; }

    [Option("source", Description = "Source name to record (default: the URL's host, Alpaca's outlet, or \"manual\").")]
    public string? Source { get; init; }

    [Option("days", Description = "News lookback for a ticker, 1-30 days (default 3).")]
    public int Days { get; init; } = 3;

    [Option("max-articles", Description = "Most news articles to analyze for a ticker, 1-20 (default 5).")]
    public int MaxArticles { get; init; } = 5;

    [Option("save", Description = "Persist the extracted claims to the research feed (skips a URL already analyzed).")]
    public bool Save { get; init; }
}

/// <summary>One catalyst or portent the LLM extracted, as <c>research analyze</c> reports it.</summary>
public sealed record AnalyzedCatalyst(
    string Ticker, string Source, string? Url, string Summary, string Type, string Sentiment, string Magnitude,
    bool Portent, string? Trigger, string? Timeline, string? TriggerConfidence, string Mechanism, Guid? ClaimId);

/// <summary>
/// On-demand catalyst extraction: sends an article, a filing page or a ticker's recent news
/// through <see cref="CatalystExtractor"/> (Legion, on the caller's Claude key) and shows each
/// catalyst and portent with its mechanism. Nothing is stored unless <c>--save</c>, which writes
/// claims exactly as the scan pass would (deduplicated by URL).
/// </summary>
[Operation("research analyze", Summary = "Extract catalysts and portents from a ticker's news, a URL or pasted text via the LLM (--save to keep them).",
    Description = "Uses your Claude key (else the host's). Without --save nothing is written; with --save the claims join the research feed like scanned ones.",
    Risk = OperationRisk.Write)]
[Example("idiotproof research analyze NVDA")]
[Example("idiotproof research analyze https://www.sec.gov/Archives/edgar/data/1045810/x.htm --ticker NVDA --save")]
[Example("idiotproof research analyze AMD --text \"AMD signs a preliminary $4B supply deal, closing next quarter\"")]
public sealed partial class ResearchAnalyzeOperation : IOperation<ResearchAnalyzeArgs>
{
    /// <summary>Most characters of a fetched page sent to the extractor (it truncates to 8,000 itself).</summary>
    public const int MaxPageChars = 8000;

    public async Task<OperationResult> ExecuteAsync(ResearchAnalyzeArgs args, OperationContext ctx)
    {
        var target = (args.Target ?? "").Trim();
        if (target.Length == 0) return OperationResult.Usage("Name a ticker or a URL to analyze.");
        if (args.Days is < 1 or > 30) return OperationResult.Usage("--days must be 1-30.");
        if (args.MaxArticles is < 1 or > 20) return OperationResult.Usage("--max-articles must be 1-20.");

        var isUrl = Uri.TryCreate(target, UriKind.Absolute, out var url) && url.Scheme is "http" or "https";
        var ticker = (isUrl ? args.Ticker : args.Ticker ?? target)?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(ticker))
            return OperationResult.Usage("A URL needs --ticker <symbol>: the company the article is about.");
        if (ticker.Length > 20 || !ticker.All(c => char.IsLetterOrDigit(c) || c is '.' or '-'))
            return OperationResult.Usage($"'{ticker}' is not a ticker symbol.");

        var key = ctx.Services.GetService<IClaudeKeyContext>() is { } keys
            ? (await keys.ResolveAsync(ctx.CancellationToken)).ClaudeApiKey
            : null;
        if (string.IsNullOrWhiteSpace(key)) return OperationResult.Blocked(UserClaudeKeyResolver.NoKeyMessage);

        // What to read: pasted text, a fetched page, or the ticker's recent news.
        var docs = new List<(string Text, string Source, string? Url, DateOnly Date, int Tier)>();
        var today = Account.AccountApis.EtDate(ctx.UtcNow);
        if (!string.IsNullOrWhiteSpace(args.Text))
            docs.Add((args.Text.Trim(), args.Source ?? (isUrl ? url!.Host : "manual"), isUrl ? target : null, today, 3));
        else if (isUrl)
            docs.Add((await FetchPageTextAsync(ctx, url!), args.Source ?? url!.Host, target, today, 3));
        else
        {
            var news = await ctx.Get<AlpacaNewsService>().GetNewsAsync(ticker, args.Days, ctx.CancellationToken);
            foreach (var a in news.Take(args.MaxArticles))
                docs.Add((string.IsNullOrWhiteSpace(a.Summary) ? a.Headline : $"{a.Headline}\n\n{a.Summary}",
                    args.Source ?? a.Source, a.Url, DateOnly.FromDateTime(a.PublishedAt), 2));
            if (docs.Count == 0)
                return OperationResult.Ok(new { Ticker = ticker, Catalysts = Array.Empty<AnalyzedCatalyst>() },
                    $"No Alpaca news for {ticker} in the last {args.Days} days. Pass --text or a URL to analyze something specific.");
        }

        var found = new List<AnalyzedCatalyst>();
        var skipped = 0;
        foreach (var doc in docs)
        {
            if (args.Save)
            {
                var saved = await ctx.Get<ResearchService>().AnalyzeArticleAsync(
                    ticker, doc.Text, doc.Source, doc.Url, doc.Tier, doc.Date, ctx.CancellationToken, key);
                if (saved.Count == 0) skipped++;
                found.AddRange(saved.Select(c => FromClaim(c, doc.Url)));
            }
            else
            {
                var extraction = await ctx.Get<CatalystExtractor>().ExtractAsync(ticker, doc.Text, doc.Source, key, ctx.CancellationToken);
                if (extraction is null || extraction.Catalysts.Count == 0) { skipped++; continue; }
                found.AddRange(extraction.Catalysts.Select(c => new AnalyzedCatalyst(
                    ticker, doc.Source, doc.Url, c.Summary, c.Type, c.Sentiment, c.Magnitude, !c.HasHappenedAlready,
                    c.PendingTrigger, c.ExpectedTimeline, c.TriggerConfidence, c.Mechanism, null)));
            }
        }

        var data = new { Ticker = ticker, Documents = docs.Count, Saved = args.Save, Catalysts = found };
        if (found.Count == 0)
            return OperationResult.Ok(data, args.Save && skipped == docs.Count
                ? $"No new claims for {ticker}: nothing extracted, or every source was already analyzed."
                : $"No catalysts found in {docs.Count} document(s) for {ticker}.");

        var lines = new List<string> { $"{ticker}: {found.Count} catalysts from {docs.Count} document(s)" + (args.Save ? ", saved." : " (not saved; --save keeps them).") };
        foreach (var c in found)
        {
            lines.Add("");
            lines.Add($"• {c.Summary}" + (c.ClaimId is { } id ? $"  [{ResearchSupport.Short(id)}]" : ""));
            lines.Add($"  {c.Type} · {c.Sentiment} · {c.Magnitude} · " + (c.Portent
                ? $"portent: {c.Trigger ?? "trigger unstated"}, {c.Timeline ?? "timeline unstated"}, {c.TriggerConfidence ?? "?"} confidence"
                : "already happened"));
            if (!string.IsNullOrWhiteSpace(c.Mechanism)) lines.Add($"  why: {c.Mechanism}");
            lines.Add($"  from: {c.Source}" + (c.Url is null ? "" : $" {c.Url}"));
        }
        return OperationResult.Ok(data, string.Join("\n", lines));
    }

    private static AnalyzedCatalyst FromClaim(ResearchClaim c, string? url) => new(
        c.Ticker, c.SourceName, url, c.ClaimSummary, c.ClaimType, c.Sentiment, c.Magnitude, !c.HasHappenedAlready,
        c.PendingTrigger, c.ExpectedTimeline, c.TriggerConfidence, c.LlmAnswer ?? "", c.Id);

    private static async Task<string> FetchPageTextAsync(OperationContext ctx, Uri url)
    {
        // SEC asks for a descriptive User-Agent; the "edgar" client carries one.
        using var http = ctx.Get<IHttpClientFactory>().CreateClient(url.Host.EndsWith("sec.gov", StringComparison.OrdinalIgnoreCase) ? "edgar" : "");
        using var resp = await http.GetAsync(url, ctx.CancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw OperationException.Failed($"{url.Host} answered {(int)resp.StatusCode} {resp.ReasonPhrase} for that URL.");
        var text = PageText(await resp.Content.ReadAsStringAsync(ctx.CancellationToken));
        if (text.Length == 0) throw OperationException.Failed("That page has no readable text.");
        return text.Length > MaxPageChars ? text[..MaxPageChars] : text;
    }

    /// <summary>Visible text of an HTML page (scripts, styles and tags removed, entities decoded, whitespace folded).</summary>
    public static string PageText(string html)
    {
        var noScripts = ScriptOrStyle().Replace(html, " ");
        var noTags = Tag().Replace(noScripts, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(noTags), " ").Trim();
    }

    [GeneratedRegex(@"<(script|style|noscript)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
