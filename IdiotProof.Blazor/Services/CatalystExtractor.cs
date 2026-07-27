using System.Text.Json;
using IdiotProof.Engine.Settings;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Services;

public record ExtractedCatalyst(
    string  Summary,
    string  Type,
    string  Sentiment,
    string  Magnitude,
    bool    HasHappenedAlready,
    string? PendingTrigger,
    string? ExpectedTimeline,
    string? TriggerConfidence,
    string  Mechanism
)
{
    /// <summary>
    /// Deterministically composes the claim's display sentence from structured
    /// fields instead of trusting one LLM-authored paragraph — the tone
    /// guarantee lives in this composition, not in a prompt instruction that
    /// can drift. Sober equity-research register: what happened, which
    /// ticker(s) it affects and why (the mechanism), and when.
    /// </summary>
    public static string ComposeSentence(string summary, string ticker, string mechanism, string? expectedTimeline)
    {
        var timing = string.IsNullOrWhiteSpace(expectedTimeline) ? "already priced in" : expectedTimeline;
        var affects = string.IsNullOrWhiteSpace(ticker) ? "the affected tickers" : ticker.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(mechanism)
            ? summary
            : $"{summary}. Affects {affects} because {mechanism}. Expected impact: {timing}.";
    }
}

public record CatalystExtraction(
    string                  Ticker,
    List<ExtractedCatalyst> Catalysts,
    string                  SourceAssessment,
    int                     SourceTierSuggestion
);

/// <summary>
/// Sends article/filing text to the LLM and extracts structured catalysts and
/// portents. A portent is an announced-but-not-yet-executed event — the system's
/// core insight: news reported 3 weeks early because the announcement preceded
/// the action. Uses Haiku for cheap, fast extraction.
/// </summary>
public sealed class CatalystExtractor(
    LegionClient legion,
    AppSettings  appSettings,
    ILogger<CatalystExtractor> logger)
{
    private const string SystemPrompt = """
        You are a financial intelligence analyst. Given article text or SEC filing content
        about a publicly traded company, extract every catalyst and portent.

        KEY CONCEPT — PORTENT vs CATALYST:
        - A PORTENT is an announced event that has NOT yet executed. Examples:
          "preliminary contract pending signature", "acquisition announced closing Q4 2026",
          "production starts 2027", "FDA application submitted (decision pending)".
          These are GOLD: the stock may not yet have priced them in, and they may move
          it weeks or months later when they actually happen.
        - A CATALYST is an event that has already occurred (earnings reported, contract
          signed, merger closed). These move the stock immediately.

        TONE — this is a sober equity-research desk, not a financial news headline:
        - State facts plainly. No hype, no clickbait, no exclamation points, no adjectives
          like "shocking", "huge", "massive", "explosive". A reader should not be able to
          tell this system is trying to grab attention.
        - Every catalyst's "mechanism" field must state the CAUSAL LINK plainly: why this
          fact moves (or doesn't move) the price. "Reduces near-term dilution risk" is
          good; "This is huge for shareholders!" is not.
        - "summary" states what happened/was announced. "mechanism" states why it matters
          to the price. Keep both factual and unembellished.

        Respond ONLY with valid JSON matching this schema exactly:
        {
          "ticker": "LMT",
          "catalysts": [
            {
              "summary": "concise description under 120 chars, factual",
              "type": "Earnings|Contract|Insider|MA|Guidance|Regulatory|News",
              "sentiment": "Bullish|Bearish|Neutral",
              "magnitude": "High|Medium|Low",
              "has_happened_already": true,
              "pending_trigger": null,
              "expected_timeline": null,
              "trigger_confidence": null,
              "mechanism": "plain statement of why this affects the price"
            },
            {
              "summary": "Preliminary $35B THAAD contract pending Pentagon finalization",
              "type": "Contract",
              "sentiment": "Bullish",
              "magnitude": "High",
              "has_happened_already": false,
              "pending_trigger": "Contract signature / Pentagon finalization",
              "expected_timeline": "weeks",
              "trigger_confidence": "High",
              "mechanism": "Adds a large, multi-year revenue stream once finalized; not yet reflected in guidance"
            }
          ],
          "source_assessment": "Primary|Editorial|Promotional",
          "source_tier": 1
        }

        source_tier: 1=Primary (SEC filing, government DB), 2=Editorial (major press),
        3=Promotional/Unknown (press releases, paid content, blogs).
        Return ONLY the JSON object. No markdown, no commentary.
        """;

    public async Task<CatalystExtraction?> ExtractAsync(
        string ticker,
        string articleText,
        string sourceName,
        CancellationToken ct = default)
    {
        var snippet = articleText.Length > 8000 ? articleText[..8000] : articleText;
        var userMsg = $"Ticker: {ticker}\nSource: {sourceName}\n\nContent:\n{snippet}";

        try
        {
            var raw = await legion.CallAsync(
                providerId:   "claude-api",
                apiKey:       appSettings.ClaudeApiKey,
                model:        "claude-haiku-4-5-20251001",
                systemPrompt: SystemPrompt,
                userMessage:  userMsg,
                maxTokens:    2000,
                temperature:  0.2,
                ct:           ct);

            var json  = raw.Trim();
            var start = json.IndexOf('{');
            var end   = json.LastIndexOf('}');
            if (start < 0 || end < 0) return null;
            json = json[start..(end + 1)];

            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var catalysts = new List<ExtractedCatalyst>();
            if (root.TryGetProperty("catalysts", out var arr))
            {
                foreach (var c in arr.EnumerateArray())
                {
                    catalysts.Add(new ExtractedCatalyst(
                        Summary:            Str(c, "summary"),
                        Type:               Str(c, "type",      "News"),
                        Sentiment:          Str(c, "sentiment", "Neutral"),
                        Magnitude:          Str(c, "magnitude", "Low"),
                        HasHappenedAlready: Bool(c, "has_happened_already"),
                        PendingTrigger:     NullStr(c, "pending_trigger"),
                        ExpectedTimeline:   NullStr(c, "expected_timeline"),
                        TriggerConfidence:  NullStr(c, "trigger_confidence"),
                        Mechanism:          Str(c, "mechanism")
                    ));
                }
            }

            return new CatalystExtraction(
                Ticker:              root.TryGetProperty("ticker",            out var t)   ? t.GetString() ?? ticker : ticker,
                Catalysts:           catalysts,
                SourceAssessment:    Str(root, "source_assessment", "Unknown"),
                SourceTierSuggestion: root.TryGetProperty("source_tier", out var st) ? st.GetInt32() : 3
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CatalystExtractor failed for {Ticker} / {Source}", ticker, sourceName);
            return null;
        }
    }

    private static string   Str(JsonElement e, string key, string fallback = "")
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static string?  NullStr(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool     Bool(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
