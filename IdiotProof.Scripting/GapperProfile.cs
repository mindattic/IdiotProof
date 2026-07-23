// ============================================================================
// GapperProfile - The adjustable "what counts as a gapper" dial-in set
// ============================================================================
//
// A profile is a TEMPLATE (static JSON catalog per IP-LAW-7, shipped at
// IdiotProof.Blazor/wwwroot/data/gapper-profiles.json). Selecting one
// pre-fills the Gapper tab's queue form; every value stays editable per
// ticker — all gappers are not the same. The tuned result is denormalized
// into the queued strategy's ScriptText via GapperScriptFactory, so the
// Strategy SQL row remains the single runtime source of truth the Monitor
// evaluates (RFC 0002 / IP-A8).
// ============================================================================

using System.Text.Json.Serialization;

namespace IdiotProof.Scripting;

/// <summary>
/// One gapper trade plan: screen criteria, entry window, risk rails, and the
/// sell-off rules that get the position flat before the 9:30 bell.
/// </summary>
public sealed class GapperProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    // ── Screen: what qualifies as a gapper ──
    /// <summary>Minimum gap over the previous day's close, in percent.</summary>
    [JsonPropertyName("minGapPercent")]
    public double MinGapPercent { get; set; } = 5;

    /// <summary>Optional gap ceiling — beyond this the move is "already gone".</summary>
    [JsonPropertyName("maxGapPercent")]
    public double? MaxGapPercent { get; set; }

    /// <summary>Premarket volume must be at least this multiple of average volume.</summary>
    [JsonPropertyName("minVolumeRatio")]
    public double MinVolumeRatio { get; set; } = 2;

    [JsonPropertyName("minPrice")]
    public double MinPrice { get; set; } = 0.50;

    [JsonPropertyName("maxPrice")]
    public double MaxPrice { get; set; } = 25;

    // ── Entry window (ET) ──
    [JsonPropertyName("entryWindowStartEt")]
    public string EntryWindowStartEt { get; set; } = "04:00";

    [JsonPropertyName("entryWindowEndEt")]
    public string EntryWindowEndEt { get; set; } = "09:00";

    // ── Risk rails ──
    /// <summary>Hard stop below entry, percent. RiskGuardian refuses stopless orders.</summary>
    [JsonPropertyName("stopLossPercent")]
    public double StopLossPercent { get; set; } = 5;

    /// <summary>Optional trailing stop, percent off the high-water mark.</summary>
    [JsonPropertyName("trailingStopPercent")]
    public double? TrailingStopPercent { get; set; }

    // ── Sell-off before the bell ──
    /// <summary>
    /// Momentum-rollover exit: sell once price gives back this % of the run
    /// from entry to peak. Scales with momentum — big runners get more room.
    /// </summary>
    [JsonPropertyName("peakGivebackPercent")]
    public double PeakGivebackPercent { get; set; } = 25;

    /// <summary>ET time from which the rollover exit is armed ("the last 15 minutes").</summary>
    [JsonPropertyName("armExitAtEt")]
    public string ArmExitAtEt { get; set; } = "09:15";

    /// <summary>Hard flatten time (ET) — always out before the 9:30 bell.</summary>
    [JsonPropertyName("sellByEt")]
    public string SellByEt { get; set; } = "09:28";

    // ── Sizing ──
    /// <summary>Default position size in dollars (Alpaca notional).</summary>
    [JsonPropertyName("defaultNotional")]
    public decimal DefaultNotional { get; set; } = 1000;

    /// <summary>Deep copy so per-ticker dial-ins never mutate the catalog template.</summary>
    public GapperProfile Clone() => (GapperProfile)MemberwiseClone();

    /// <summary>
    /// Validates the dial-in set. Returns the list of problems; empty = valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (MinGapPercent <= 0) problems.Add("Min gap % must be positive.");
        if (MaxGapPercent is { } maxGap && maxGap <= MinGapPercent) problems.Add("Max gap % must exceed min gap %.");
        if (MinVolumeRatio <= 0) problems.Add("Min volume ratio must be positive.");
        if (MinPrice < 0 || MaxPrice <= MinPrice) problems.Add("Price band must satisfy 0 <= min < max.");
        if (StopLossPercent <= 0) problems.Add("Stop loss % must be positive — RiskGuardian refuses stopless orders.");
        if (TrailingStopPercent is { } tsl && tsl <= 0) problems.Add("Trailing stop %, when set, must be positive.");
        if (PeakGivebackPercent is <= 0 or > 100) problems.Add("Peak giveback % must be in (0, 100].");
        if (DefaultNotional <= 0) problems.Add("Notional must be positive.");
        problems.AddRange(ValidateTime(EntryWindowStartEt, "Entry window start"));
        problems.AddRange(ValidateTime(EntryWindowEndEt, "Entry window end"));
        problems.AddRange(ValidateTime(ArmExitAtEt, "Arm-exit time"));
        problems.AddRange(ValidateTime(SellByEt, "Sell-by time"));

        // Cross-field ordering: only meaningful when the individual times parse.
        if (TryTime(EntryWindowStartEt, out var winStart) && TryTime(EntryWindowEndEt, out var winEnd) && winStart >= winEnd)
            problems.Add("Entry window start must be before the entry window end — an inverted window is " +
                         "evaluated as an overnight wrap and would open entries outside the intended premarket slot.");
        if (TryTime(ArmExitAtEt, out var arm) && TryTime(SellByEt, out var sellBy) && arm >= sellBy)
            problems.Add("Arm-exit time must be before the sell-by time — otherwise the momentum-rollover " +
                         "exit can never fire and every exit is the hard flatten.");
        if (TryTime(EntryWindowEndEt, out var winEnd2) && TryTime(SellByEt, out var sb) && winEnd2 > sb)
            problems.Add("Entry window end must not exceed the sell-by time — entries accepted after the " +
                         "hard flatten cannot be closed by any configured exit.");

        return problems;
    }

    private static bool TryTime(string value, out TimeSpan time)
    {
        try { time = StrategyBuilder.ParseTimeOfDay(value); return true; }
        catch (FormatException) { time = default; return false; }
    }

    private static IEnumerable<string> ValidateTime(string value, string label)
    {
        string? problem = null;
        try { StrategyBuilder.ParseTimeOfDay(value); }
        catch (FormatException ex) { problem = $"{label}: {ex.Message}"; }
        if (problem is not null) yield return problem;
    }
}

/// <summary>
/// Turns (ticker + tuned profile) into canonical IdiotScript. The output is
/// produced by the real StrategyBuilder and serialized with ToScript(), so it
/// is round-trip-safe through ScriptParser by construction.
/// </summary>
public static class GapperScriptFactory
{
    /// <summary>
    /// Builds the gapper StrategyDefinition for a ticker from a tuned profile.
    /// </summary>
    public static StrategyBuilder Compose(string symbol, GapperProfile p)
    {
        var problems = p.Validate();
        if (problems.Count > 0)
            throw new ArgumentException($"Gapper profile '{p.Name}' is invalid: {string.Join(" ", problems)}");

        var builder = Stock.Ticker(symbol)
            .Name($"{symbol.ToUpperInvariant()} Gapper — {p.Name}")
            .Session(Models.TradingSession.Premarket)
            .RequireEntryWindow(p.EntryWindowStartEt, p.EntryWindowEndEt);

        if (p.MaxGapPercent is { } maxGap)
            builder.IsGapBetween(p.MinGapPercent, maxGap);
        else
            builder.IsGapUp(p.MinGapPercent);

        builder
            .IsVolumeAbove(p.MinVolumeRatio)
            .IsPriceBetween(p.MinPrice, p.MaxPrice)
            .Long()
            .QuantityShares(1)
            .StopLossPercent(p.StopLossPercent);

        if (p.TrailingStopPercent is { } tsl)
            builder.TrailingStopLoss(tsl);

        builder
            .PeakGiveback(p.PeakGivebackPercent, p.ArmExitAtEt)
            .SellBy(p.SellByEt);

        return builder;
    }

    /// <summary>Generates the canonical, parser-round-trippable script text.</summary>
    public static string ToScript(string symbol, GapperProfile p) => Compose(symbol, p).ToScript();
}
