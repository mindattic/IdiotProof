using System.Text;
using System.Text.Json;
using IdiotProof.Engine.Settings;
using IdiotProof.Scripting;
using MindAttic.Legion;

namespace IdiotProof.Blazor.Services;

/// <summary>One interpreted gapper candidate, ready for human review.</summary>
public sealed record GapperCandidate(string Symbol, GapperProfile Profile, string Rationale);

/// <summary>Result of interpreting a transcript. Candidates are review-only — nothing is queued automatically.</summary>
public sealed record GapperInterpretation(
    bool Success,
    IReadOnlyList<GapperCandidate> Candidates,
    IReadOnlyList<string> Warnings,
    string? Error);

/// <summary>
/// Turns free-form natural language — typically a video transcript — into
/// reviewable gapper candidates. All LLM traffic routes through
/// MindAttic.Legion (HOUSE-LAW-4); the model is instructed to emit a STRICT
/// JSON array against the GapperProfile schema, and everything it returns is
/// re-validated here (symbol shape, GapperProfile.Validate, candidate cap)
/// so a hallucinated field can never reach a strategy row unchecked.
///
/// Deliberately review-first: the caller renders candidates as cards and the
/// human queues each one — a transcript is untrusted input and must never
/// place itself into the trading queue (IP-LAW-1 spirit).
/// </summary>
public sealed class GapperInterpreter(
    LegionClient legion,
    AppSettings appSettings,
    GapperProfileService profiles,
    ILogger<GapperInterpreter> logger)
{
    private const int MaxCandidates = 5;

    public async Task<GapperInterpretation> InterpretAsync(string transcript, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return new GapperInterpretation(false, [], [], "Paste a transcript or describe the plays first.");

        if (string.IsNullOrWhiteSpace(appSettings.ClaudeApiKey))
            return new GapperInterpretation(false, [], [],
                "No Claude API key configured. Set it on the API Keys page or in the MindAttic LLM keyring.");

        try
        {
            var content = await legion.CallAsync(
                providerId: "claude-api",
                apiKey: appSettings.ClaudeApiKey,
                model: appSettings.LlmVoterModel ?? "claude-sonnet-5",
                systemPrompt: BuildSystemPrompt(BaseProfile()),
                userMessage: transcript,
                // Headroom for 5 full candidates with verbose rationales —
                // 2000 could truncate mid-array, and a missing ']' loses
                // EVERY candidate, not just the last one.
                maxTokens: 4000,
                ct: ct).ConfigureAwait(false);

            var (candidates, warnings) = ParseCandidates(content, BaseProfile());
            if (candidates.Count == 0)
                return new GapperInterpretation(false, [], warnings,
                    warnings.Count > 0
                        ? "No usable gapper candidates survived validation — see warnings."
                        : "The text didn't describe any premarket gap plays I could turn into a gapper.");

            return new GapperInterpretation(true, candidates, warnings, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gapper transcript interpretation failed.");
            return new GapperInterpretation(false, [], [], $"Interpretation error: {ex.Message}");
        }
    }

    private GapperProfile BaseProfile() =>
        profiles.GetById("classic-gapper")?.Clone() ?? new GapperProfile { Id = "classic-gapper", Name = "Classic Gapper" };

    /// <summary>
    /// The extraction contract. Field list and defaults are written from the
    /// live base profile so the prompt can't drift from the catalog.
    /// </summary>
    internal static string BuildSystemPrompt(GapperProfile baseProfile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You extract PREMARKET GAP TRADE candidates (\"gappers\") from trader talk — video transcripts, chat, notes.");
        sb.AppendLine("A gapper: a stock expected to gap up in premarket that the speaker would buy early (~4:00-9:00 AM ET) and sell before the 9:30 bell.");
        sb.AppendLine();
        sb.AppendLine("Respond with ONLY a JSON array (no prose, no code fences). Each element:");
        sb.AppendLine("""
{
  "symbol": "TICKER",
  "rationale": "one sentence: why the speaker likes it + which dials you changed and why",
  "profile": {
    "minGapPercent": number, "maxGapPercent": number|null,
    "minVolumeRatio": number, "minPrice": number, "maxPrice": number,
    "entryWindowStartEt": "HH:mm", "entryWindowEndEt": "HH:mm",
    "stopLossPercent": number, "trailingStopPercent": number|null,
    "peakGivebackPercent": number, "armExitAtEt": "HH:mm", "sellByEt": "HH:mm",
    "defaultNotional": number
  }
}
""");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Include ONLY tickers the text actually presents as premarket gap/momentum plays. No index ETFs unless explicitly called a gap play. Never invent tickers.");
        sb.AppendLine($"- At most {MaxCandidates} candidates, best first. If the text describes none, return [].");
        sb.AppendLine("- Inside \"profile\", include ONLY the fields the text gives you a concrete reason to change; omit the rest.");
        sb.AppendLine("- Defaults used for omitted fields (the Classic Gapper profile):");
        sb.AppendLine($"  minGapPercent={Inv(baseProfile.MinGapPercent)}, maxGapPercent={(baseProfile.MaxGapPercent is { } mg ? Inv(mg) : "null")}, " +
                      $"minVolumeRatio={Inv(baseProfile.MinVolumeRatio)}, minPrice={Inv(baseProfile.MinPrice)}, maxPrice={Inv(baseProfile.MaxPrice)},");
        sb.AppendLine($"  entryWindow={baseProfile.EntryWindowStartEt}-{baseProfile.EntryWindowEndEt}, stopLossPercent={Inv(baseProfile.StopLossPercent)}, " +
                      $"peakGivebackPercent={Inv(baseProfile.PeakGivebackPercent)}, armExitAtEt={baseProfile.ArmExitAtEt}, sellByEt={baseProfile.SellByEt}, " +
                      $"defaultNotional={baseProfile.DefaultNotional}");
        sb.AppendLine("- Mapping hints: \"tight stop\" -> lower stopLossPercent; \"let it run / big runner\" -> higher peakGivebackPercent; " +
                      "\"take profits quick / scalp\" -> lower peakGivebackPercent and/or earlier armExitAtEt; \"low float / penny\" -> lower price band, higher minVolumeRatio and stopLossPercent; " +
                      "explicit gap sizes (\"up 12% premarket\") -> set minGapPercent a bit below the quoted gap.");
        sb.AppendLine("- Times are US Eastern \"HH:mm\". armExitAtEt must be BEFORE sellByEt; sellByEt must be before 09:30.");
        sb.AppendLine("- All exits happen before the 9:30 bell. Do not design swing or RTH strategies here.");
        return sb.ToString();
    }

    /// <summary>
    /// Parses the model's response into validated candidates. Pure and static
    /// so it is unit-testable without Legion. Anything malformed is skipped
    /// with a warning — never guessed at.
    /// </summary>
    internal static (List<GapperCandidate> Candidates, List<string> Warnings) ParseCandidates(string content, GapperProfile baseProfile)
    {
        var candidates = new List<GapperCandidate>();
        var warnings = new List<string>();

        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            // Distinguish "no array at all" from "array started but never
            // closed" — the latter means the model hit the token cap and the
            // user should know shortening the transcript will help.
            warnings.Add(start >= 0
                ? "The response was cut off before the candidate list finished (no closing ']') — try a shorter transcript or fewer plays."
                : "Response contained no JSON array.");
            return (candidates, warnings);
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(content[start..(end + 1)]); }
        catch (JsonException ex)
        {
            warnings.Add($"Response was not valid JSON: {ex.Message}");
            return (candidates, warnings);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Response JSON was not an array.");
                return (candidates, warnings);
            }

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (candidates.Count >= MaxCandidates)
                {
                    warnings.Add($"More than {MaxCandidates} candidates returned — extras ignored.");
                    break;
                }

                var symbol = GetStringCI(el, "symbol")?.Trim().ToUpperInvariant() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(symbol, "^[A-Z]{1,6}$"))
                {
                    warnings.Add($"Skipped candidate with invalid symbol '{symbol}'.");
                    continue;
                }

                var profile = baseProfile.Clone();
                profile.Name = $"Interpreted — {symbol}";
                if (TryGetPropertyCI(el, "profile", out var profileEl) && profileEl.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        // Overlay: only the fields present in the JSON change;
                        // everything else keeps the base profile's value.
                        var overlay = JsonSerializer.Deserialize<GapperProfile>(profileEl.GetRawText(), OverlayOpts);
                        if (overlay is not null) ApplyOverlay(profile, profileEl, overlay);
                    }
                    catch (JsonException ex)
                    {
                        warnings.Add($"{symbol}: profile block unreadable ({ex.Message}) — using defaults.");
                    }
                }

                var problems = profile.Validate();
                if (problems.Count > 0)
                {
                    warnings.Add($"Skipped {symbol}: {string.Join(" ", problems)}");
                    continue;
                }

                var rationale = GetStringCI(el, "rationale") ?? "";
                candidates.Add(new GapperCandidate(symbol, profile, rationale));
            }
        }

        return (candidates, warnings);
    }

    // AllowReadingFromString: LLMs frequently emit numeric fields as strings
    // ("minGapPercent": "7"). Without this, a single stringified number threw
    // a JsonException that dropped the ENTIRE profile overlay for that
    // candidate back to base defaults (silently losing the model's tuning).
    private static readonly JsonSerializerOptions OverlayOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Copies onto <paramref name="target"/> only the properties actually
    /// present in the JSON object — a plain Deserialize would stomp absent
    /// fields back to class defaults, losing the base profile's values.
    /// </summary>
    private static void ApplyOverlay(GapperProfile target, JsonElement el, GapperProfile parsed)
    {
        if (Has(el, "minGapPercent")) target.MinGapPercent = parsed.MinGapPercent;
        if (Has(el, "maxGapPercent")) target.MaxGapPercent = parsed.MaxGapPercent;
        if (Has(el, "minVolumeRatio")) target.MinVolumeRatio = parsed.MinVolumeRatio;
        if (Has(el, "minPrice")) target.MinPrice = parsed.MinPrice;
        if (Has(el, "maxPrice")) target.MaxPrice = parsed.MaxPrice;
        if (Has(el, "entryWindowStartEt")) target.EntryWindowStartEt = parsed.EntryWindowStartEt;
        if (Has(el, "entryWindowEndEt")) target.EntryWindowEndEt = parsed.EntryWindowEndEt;
        if (Has(el, "stopLossPercent")) target.StopLossPercent = parsed.StopLossPercent;
        if (Has(el, "trailingStopPercent")) target.TrailingStopPercent = parsed.TrailingStopPercent;
        if (Has(el, "peakGivebackPercent")) target.PeakGivebackPercent = parsed.PeakGivebackPercent;
        if (Has(el, "armExitAtEt")) target.ArmExitAtEt = parsed.ArmExitAtEt;
        if (Has(el, "sellByEt")) target.SellByEt = parsed.SellByEt;
        if (Has(el, "defaultNotional")) target.DefaultNotional = parsed.DefaultNotional;
    }

    private static bool Has(JsonElement el, string name) => TryGetPropertyCI(el, name, out _);

    private static string? GetStringCI(JsonElement el, string name) =>
        TryGetPropertyCI(el, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryGetPropertyCI(JsonElement el, string name, out JsonElement value)
    {
        if (el.TryGetProperty(name, out value)) return true;
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
