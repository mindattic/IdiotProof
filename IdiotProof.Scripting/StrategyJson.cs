// ============================================================================
// StrategyJson - the CANONICAL strategy format (IP-A13 / IP-LAW-8)
// ============================================================================
//
// The semantic model (StrategyDefinition) is the source of truth; IdiotScript
// text is a human-facing VIEW generated from it. This file is the versioned,
// STRICT JSON round trip of the model:
//
//   • Serialize: every field the evaluators read, including composed
//     conditions and branching — things the text round trip historically lost.
//   • Deserialize: fail closed. Unknown schema version, unknown condition
//     type, unknown property, malformed value → StrategyJsonException. A
//     strategy the reader does not FULLY understand is never partially
//     evaluated ("parse, don't validate"; no tolerant shotgun parsing on the
//     money path).
//
// The tolerant regex ScriptParser remains only for (a) legacy rows written
// before ScriptJson existed and (b) hand-typed script text, until the strict
// Roslyn parser (IP-US-H1) replaces it.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Models;

namespace IdiotProof.Scripting;

/// <summary>Thrown when canonical strategy JSON cannot be FULLY understood.</summary>
public sealed class StrategyJsonException(string message) : Exception(message);

public static class StrategyJson
{
    public const int CurrentSchemaVersion = 1;

    // ── Serialize ───────────────────────────────────────────────────────

    public static string Serialize(StrategyDefinition def)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = CurrentSchemaVersion,
            ["symbol"] = def.Symbol,
            ["name"] = def.Name,
            ["session"] = def.Session.ToString(),
            ["quantity"] = def.Quantity,
            ["notionalAmount"] = def.NotionalAmount,
            ["direction"] = def.Direction.ToString(),
            ["entryConditions"] = new JsonArray(def.EntryConditions.Select(WriteCondition).ToArray()),
            ["takeProfitPrice"] = def.TakeProfitPrice,
            ["takeProfitPercent"] = def.TakeProfitPercent,
            ["takeProfitTargets"] = new JsonArray(def.TakeProfitTargets.Select(WriteTarget).ToArray()),
            ["stopLossPrice"] = def.StopLossPrice,
            ["stopLossPercent"] = def.StopLossPercent,
            ["trailingStopPercent"] = def.TrailingStopPercent,
            ["exitTime"] = def.ExitTime is { } et ? Time(et) : null,
            ["peakGivebackPercent"] = def.PeakGivebackPercent,
            ["peakGivebackArmTime"] = def.PeakGivebackArmTime is { } arm ? Time(arm) : null,
            ["exitAtPriorHigh"] = def.ExitAtPriorHigh,
            ["isAutonomous"] = def.IsAutonomous,
            ["isAdaptive"] = def.IsAdaptive,
            ["shouldRepeat"] = def.ShouldRepeat,
            ["conditionalBlocks"] = new JsonArray(def.ConditionalBlocks.Select(WriteBlock).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Time(TimeSpan t) => t.ToString(@"hh\:mm");

    private static JsonNode WriteCondition(ICondition c) => c switch
    {
        AndCondition a => new JsonObject { ["type"] = "and", ["left"] = WriteCondition(a.Left), ["right"] = WriteCondition(a.Right) },
        OrCondition o => new JsonObject { ["type"] = "or", ["left"] = WriteCondition(o.Left), ["right"] = WriteCondition(o.Right) },
        NotCondition n => new JsonObject { ["type"] = "not", ["inner"] = WriteCondition(n.Inner) },
        IndicatorCondition i => new JsonObject
        {
            ["type"] = "indicator",
            ["indicator"] = i.Type.ToString(),
            ["p1"] = i.Parameter,
            ["p2"] = i.Parameter2,
            ["phase"] = i.Phase.ToString(),
        },
        PatternCondition p => new JsonObject { ["type"] = "pattern", ["pattern"] = p.Type.ToString(), ["level"] = p.Level },
        PriceCondition pc => new JsonObject { ["type"] = "entryPrice", ["price"] = pc.Price },
        PriceLevelCondition pl => new JsonObject
        {
            ["type"] = "priceLevel",
            ["kind"] = pl.Type.ToString(),
            ["level"] = pl.Level,
            ["tolerancePercent"] = pl.TolerancePercent,
        },
        TimeWindowCondition tw => new JsonObject { ["type"] = "timeWindow", ["startEt"] = Time(tw.StartEt), ["endEt"] = Time(tw.EndEt) },
        PriceBandCondition pb => new JsonObject { ["type"] = "priceBand", ["min"] = pb.Min, ["max"] = pb.Max },
        GapBandCondition gb => new JsonObject { ["type"] = "gapBand", ["minPercent"] = gb.MinPercent, ["maxPercent"] = gb.MaxPercent },
        _ => throw new StrategyJsonException($"Cannot serialize unknown condition type '{c.GetType().Name}'."),
    };

    private static JsonNode WriteTarget(TakeProfitTarget t) => new JsonObject
    {
        ["label"] = t.Label,
        ["price"] = t.Price,
        ["percentToSell"] = t.PercentToSell,
    };

    private static JsonNode WriteBlock(ConditionalBlock block) => new JsonObject
    {
        ["branches"] = new JsonArray(block.Branches.Select(b => (JsonNode)new JsonObject
        {
            ["condition"] = b.Condition is null ? null : WriteCondition(b.Condition),
            ["overrides"] = WriteOverrides(b.Overrides),
        }).ToArray()),
    };

    private static JsonNode WriteOverrides(StrategyOverrides o) => new JsonObject
    {
        ["direction"] = o.Direction?.ToString(),
        ["entryConditions"] = new JsonArray(o.EntryConditions.Select(WriteCondition).ToArray()),
        ["takeProfitPrice"] = o.TakeProfitPrice,
        ["takeProfitTargets"] = new JsonArray(o.TakeProfitTargets.Select(WriteTarget).ToArray()),
        ["stopLossPrice"] = o.StopLossPrice,
        ["stopLossPercent"] = o.StopLossPercent,
        ["trailingStopPercent"] = o.TrailingStopPercent,
    };

    // ── Deserialize (strict, fail-closed) ───────────────────────────────

    public static StrategyDefinition Deserialize(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new StrategyJsonException($"Not valid JSON: {ex.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new StrategyJsonException("Root must be a JSON object.");

            var version = GetInt(root, "schemaVersion") ?? throw new StrategyJsonException("Missing schemaVersion.");
            if (version != CurrentSchemaVersion)
                throw new StrategyJsonException($"Unsupported schemaVersion {version} (this build understands {CurrentSchemaVersion}).");

            RequireOnly(root, "strategy",
                "schemaVersion", "symbol", "name", "session", "quantity", "notionalAmount", "direction",
                "entryConditions", "takeProfitPrice", "takeProfitPercent", "takeProfitTargets",
                "stopLossPrice", "stopLossPercent", "trailingStopPercent", "exitTime",
                "peakGivebackPercent", "peakGivebackArmTime", "exitAtPriorHigh", "isAutonomous", "isAdaptive",
                "shouldRepeat", "conditionalBlocks");

            var symbol = GetString(root, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
                throw new StrategyJsonException("Missing or empty symbol.");

            var builderSeed = Stock.Ticker(symbol!).Build(); // just for the ctor path; fields set below
            var def = builderSeed;

            def.Name = GetString(root, "name");
            def.Session = GetEnum<TradingSession>(root, "session") ?? TradingSession.RTH;
            def.Quantity = GetInt(root, "quantity") ?? 0;
            def.NotionalAmount = GetDecimal(root, "notionalAmount");
            def.Direction = GetEnum<TradeDirection>(root, "direction") ?? TradeDirection.Long;
            def.TakeProfitPrice = GetDouble(root, "takeProfitPrice");
            def.TakeProfitPercent = GetDouble(root, "takeProfitPercent");
            def.StopLossPrice = GetDouble(root, "stopLossPrice");
            def.StopLossPercent = GetDouble(root, "stopLossPercent");
            def.TrailingStopPercent = GetDouble(root, "trailingStopPercent");
            def.ExitTime = GetTime(root, "exitTime");
            def.PeakGivebackPercent = GetDouble(root, "peakGivebackPercent");
            def.PeakGivebackArmTime = GetTime(root, "peakGivebackArmTime");
            def.ExitAtPriorHigh = GetBool(root, "exitAtPriorHigh") ?? false;
            def.IsAutonomous = GetBool(root, "isAutonomous") ?? false;
            def.IsAdaptive = GetBool(root, "isAdaptive") ?? false;
            def.ShouldRepeat = GetBool(root, "shouldRepeat") ?? false;

            if (root.TryGetProperty("entryConditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
                foreach (var el in conds.EnumerateArray())
                    def.EntryConditions.Add(ReadCondition(el));

            if (root.TryGetProperty("takeProfitTargets", out var targets) && targets.ValueKind == JsonValueKind.Array)
                foreach (var el in targets.EnumerateArray())
                    def.TakeProfitTargets.Add(ReadTarget(el));

            if (root.TryGetProperty("conditionalBlocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
                foreach (var el in blocks.EnumerateArray())
                    def.ConditionalBlocks.Add(ReadBlock(el));

            return def;
        }
    }

    private static ICondition ReadCondition(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw new StrategyJsonException("Condition must be a JSON object.");
        var type = GetString(el, "type") ?? throw new StrategyJsonException("Condition missing 'type'.");

        switch (type)
        {
            case "and":
                RequireOnly(el, "and-condition", "type", "left", "right");
                return new AndCondition(ReadCondition(Req(el, "left")), ReadCondition(Req(el, "right")));
            case "or":
                RequireOnly(el, "or-condition", "type", "left", "right");
                return new OrCondition(ReadCondition(Req(el, "left")), ReadCondition(Req(el, "right")));
            case "not":
                RequireOnly(el, "not-condition", "type", "inner");
                return new NotCondition(ReadCondition(Req(el, "inner")));
            case "indicator":
            {
                RequireOnly(el, "indicator-condition", "type", "indicator", "p1", "p2", "phase");
                var indicator = GetEnum<IndicatorType>(el, "indicator")
                    ?? throw new StrategyJsonException($"Unknown indicator '{GetString(el, "indicator")}'.");
                var phase = GetEnum<StrategyPhase>(el, "phase") ?? StrategyPhase.Entry;
                return new IndicatorCondition(indicator, GetDouble(el, "p1"), GetDouble(el, "p2"), phase);
            }
            case "pattern":
            {
                RequireOnly(el, "pattern-condition", "type", "pattern", "level");
                var pattern = GetEnum<PatternType>(el, "pattern")
                    ?? throw new StrategyJsonException($"Unknown pattern '{GetString(el, "pattern")}'.");
                return new PatternCondition(pattern, GetDouble(el, "level"));
            }
            case "entryPrice":
                RequireOnly(el, "entryPrice-condition", "type", "price");
                return new PriceCondition(ConditionType.Entry,
                    GetDouble(el, "price") ?? throw new StrategyJsonException("entryPrice condition missing 'price'."));
            case "priceLevel":
            {
                RequireOnly(el, "priceLevel-condition", "type", "kind", "level", "tolerancePercent");
                var kind = GetEnum<PriceLevelType>(el, "kind")
                    ?? throw new StrategyJsonException($"Unknown price-level kind '{GetString(el, "kind")}'.");
                var level = GetDouble(el, "level") ?? throw new StrategyJsonException("priceLevel condition missing 'level'.");
                return new PriceLevelCondition(kind, level, GetDouble(el, "tolerancePercent") ?? 1.0);
            }
            case "timeWindow":
                RequireOnly(el, "timeWindow-condition", "type", "startEt", "endEt");
                return new TimeWindowCondition(
                    GetTime(el, "startEt") ?? throw new StrategyJsonException("timeWindow missing 'startEt'."),
                    GetTime(el, "endEt") ?? throw new StrategyJsonException("timeWindow missing 'endEt'."));
            case "priceBand":
                RequireOnly(el, "priceBand-condition", "type", "min", "max");
                return new PriceBandCondition(
                    GetDouble(el, "min") ?? throw new StrategyJsonException("priceBand missing 'min'."),
                    GetDouble(el, "max") ?? throw new StrategyJsonException("priceBand missing 'max'."));
            case "gapBand":
                RequireOnly(el, "gapBand-condition", "type", "minPercent", "maxPercent");
                return new GapBandCondition(
                    GetDouble(el, "minPercent") ?? throw new StrategyJsonException("gapBand missing 'minPercent'."),
                    GetDouble(el, "maxPercent") ?? throw new StrategyJsonException("gapBand missing 'maxPercent'."));
            default:
                throw new StrategyJsonException($"Unknown condition type '{type}' — refusing to partially evaluate this strategy.");
        }
    }

    private static TakeProfitTarget ReadTarget(JsonElement el)
    {
        RequireOnly(el, "takeProfitTarget", "label", "price", "percentToSell");
        return new TakeProfitTarget
        {
            Label = GetString(el, "label") ?? "T1",
            Price = GetDouble(el, "price") ?? throw new StrategyJsonException("takeProfitTarget missing 'price'."),
            PercentToSell = GetInt(el, "percentToSell") ?? 100,
        };
    }

    private static ConditionalBlock ReadBlock(JsonElement el)
    {
        RequireOnly(el, "conditionalBlock", "branches");
        var block = new ConditionalBlock();
        if (el.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in branches.EnumerateArray())
            {
                RequireOnly(b, "branch", "condition", "overrides");
                var branch = new ConditionalBranch
                {
                    Condition = b.TryGetProperty("condition", out var c) && c.ValueKind != JsonValueKind.Null
                        ? ReadCondition(c) : null,
                    Overrides = ReadOverrides(Req(b, "overrides")),
                };
                block.Branches.Add(branch);
            }
        }
        return block;
    }

    private static StrategyOverrides ReadOverrides(JsonElement el)
    {
        RequireOnly(el, "overrides",
            "direction", "entryConditions", "takeProfitPrice", "takeProfitTargets",
            "stopLossPrice", "stopLossPercent", "trailingStopPercent");
        var o = new StrategyOverrides
        {
            Direction = GetEnum<TradeDirection>(el, "direction"),
            TakeProfitPrice = GetDouble(el, "takeProfitPrice"),
            StopLossPrice = GetDouble(el, "stopLossPrice"),
            StopLossPercent = GetDouble(el, "stopLossPercent"),
            TrailingStopPercent = GetDouble(el, "trailingStopPercent"),
        };
        if (el.TryGetProperty("entryConditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
            foreach (var c in conds.EnumerateArray())
                o.EntryConditions.Add(ReadCondition(c));
        if (el.TryGetProperty("takeProfitTargets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            foreach (var t in targets.EnumerateArray())
                o.TakeProfitTargets.Add(ReadTarget(t));
        return o;
    }

    // ── strict readers ──────────────────────────────────────────────────

    /// <summary>Every property must be on the allow-list — must-understand, not must-ignore.</summary>
    private static void RequireOnly(JsonElement el, string context, params string[] allowed)
    {
        foreach (var prop in el.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name, StringComparer.Ordinal))
                throw new StrategyJsonException($"Unknown property '{prop.Name}' in {context} — refusing to guess at its meaning.");
        }
    }

    private static JsonElement Req(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? v : throw new StrategyJsonException($"Missing required property '{name}'.");

    // Absent or explicit-null → null (Serialize writes nulls for unset
    // fields). Present with the wrong kind or an unrepresentable value →
    // StrategyJsonException, never a silent default and never a raw
    // FormatException that would escape StrategyLoader's quarantine net and
    // crash the evaluator instead of quarantining the row.

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String)
            throw new StrategyJsonException($"'{name}' must be a string.");
        return v.GetString();
    }

    private static bool? GetBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new StrategyJsonException($"'{name}' must be a boolean.");
        return v.GetBoolean();
    }

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i))
            throw new StrategyJsonException($"'{name}' must be a 32-bit integer.");
        return i;
    }

    private static double? GetDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d))
            throw new StrategyJsonException($"'{name}' must be a finite number.");
        return d;
    }

    private static decimal? GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d))
            throw new StrategyJsonException($"'{name}' is not representable as a decimal.");
        return d;
    }

    private static TimeSpan? GetTime(JsonElement el, string name)
    {
        var s = GetString(el, name);
        if (s is null) return null;
        try { return StrategyBuilder.ParseTimeOfDay(s); }
        catch (FormatException ex) { throw new StrategyJsonException($"Bad time in '{name}': {ex.Message}"); }
    }

    private static TEnum? GetEnum<TEnum>(JsonElement el, string name) where TEnum : struct, Enum
    {
        var s = GetString(el, name);
        if (s is null) return null;
        if (Enum.TryParse<TEnum>(s, ignoreCase: false, out var value)) return value;
        throw new StrategyJsonException($"'{s}' is not a valid {typeof(TEnum).Name} in '{name}'.");
    }
}
