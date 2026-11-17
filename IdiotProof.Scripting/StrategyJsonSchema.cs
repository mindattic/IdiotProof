// ============================================================================
// StrategyJson.Schema - the canonical strategy format as a JSON Schema (IP-T526)
// ============================================================================
//
// Generated from the strict reader's own allow-lists (DefinitionKeys, RootKeys,
// ConditionKeys, TargetKeys, BlockKeys, BranchKeys, OverridesKeys), so the set
// of keys in the schema is the set the reader accepts, by construction. What
// each key holds (its JSON type, whether null is allowed, whether it is
// required, its enum names) is declared in the tables below, next to nothing
// else, and StrategyJsonSchemaTests holds them to the reader's behaviour: a
// document the reader rejects for a missing key or a wrong kind of value must
// be rejected by the schema too.
//
// The reader stays the authority. The schema is deliberately no looser than
// the reader on what it expresses, and it is stricter in a few places where
// the reader is tolerant (see the "$comment" in the schema). A key in an
// allow-list with no entry below is published as "any value" with a note, and
// the contract test fails until it gets one.
// ============================================================================

using System.Text.Json.Nodes;
using IdiotProof.Models;

namespace IdiotProof.Scripting;

public static partial class StrategyJson
{
    public const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>The <c>$id</c> of the schema; the number is <see cref="CurrentSchemaVersion"/>.</summary>
    public static string SchemaId => $"idiotproof:strategy-canon:{CurrentSchemaVersion}";

    /// <summary>
    /// Every object the strict reader accepts and the keys it allows in each, straight from the
    /// reader's allow-lists: <c>strategy</c> (the root), <c>step</c> (an item of <c>nextSteps</c>),
    /// <c>condition:&lt;type&gt;</c> for each condition type, <c>takeProfitTarget</c>,
    /// <c>conditionalBlock</c>, <c>branch</c> and <c>overrides</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> AcceptedKeys()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["strategy"] = RootKeys,
            ["step"] = DefinitionKeys,
        };
        foreach (var (type, keys) in ConditionKeys) map["condition:" + type] = keys;
        map["takeProfitTarget"] = TargetKeys;
        map["conditionalBlock"] = BlockKeys;
        map["branch"] = BranchKeys;
        map["overrides"] = OverridesKeys;
        return map;
    }

    /// <summary>The <c>$defs</c> name of an <see cref="AcceptedKeys"/> object (null for <c>strategy</c>, which is the schema root).</summary>
    public static string? SchemaDefinitionName(string acceptedObject) =>
        acceptedObject == "strategy" ? null
        : acceptedObject.StartsWith("condition:", StringComparison.Ordinal) ? acceptedObject["condition:".Length..] + "Condition"
        : acceptedObject;

    /// <summary>
    /// The JSON Schema (draft 2020-12) of the canonical strategy JSON that <see cref="Deserialize"/>
    /// reads and <see cref="Serialize"/> writes. A fresh copy each call.
    /// </summary>
    public static JsonObject Schema()
    {
        var defs = new JsonObject
        {
            ["step"] = ObjectSchema(DefinitionKeys, DefinitionSpecs,
                "One later step of a sequence (an item of nextSteps). Same keys as the strategy, without schemaVersion and nextSteps; it trades the strategy's symbol."),
            ["condition"] = new JsonObject
            {
                ["description"] = "One condition; 'type' says which. An unknown type is refused, never skipped.",
                ["oneOf"] = new JsonArray([.. ConditionKeys.Keys.Select(type => (JsonNode)Ref(type + "Condition"))]),
            },
        };
        foreach (var (type, keys) in ConditionKeys)
        {
            var specs = ConditionSpecs.GetValueOrDefault(type) ?? new Dictionary<string, KeySpec>();
            var schema = ObjectSchema(keys, specs, ConditionDescriptions.GetValueOrDefault(type));
            // The discriminator is the one key every condition has, whatever the table says about the rest.
            ((JsonObject)schema["properties"]!)["type"] = new JsonObject { ["const"] = type };
            if (schema["required"] is not JsonArray required) schema["required"] = required = [];
            if (!required.Any(r => r!.GetValue<string>() == "type")) required.Insert(0, "type");
            defs[type + "Condition"] = schema;
        }
        defs["takeProfitTarget"] = ObjectSchema(TargetKeys, TargetSpecs, "One scale-out target.");
        defs["conditionalBlock"] = ObjectSchema(BlockKeys, BlockSpecs, "One If/ElseIf/Else block: the first branch whose condition holds supplies its overrides.");
        defs["branch"] = ObjectSchema(BranchKeys, BranchSpecs, "One branch of a conditional block.");
        defs["overrides"] = ObjectSchema(OverridesKeys, OverridesSpecs, "What a branch changes when it is chosen; a null or empty value leaves the strategy's own setting.");

        var root = ObjectSchema(RootKeys, DefinitionSpecs,
            "The canonical strategy JSON (IP-LAW-8): the versioned, strict form of a strategy. The reader fails closed: an unknown key, an unknown condition type or a value of the wrong kind refuses the whole strategy.");
        var result = new JsonObject
        {
            ["$schema"] = SchemaDialect,
            ["$id"] = SchemaId,
            ["title"] = "IdiotProof canonical strategy JSON, schema version " + CurrentSchemaVersion,
            ["$comment"] = "Generated from the strict reader's allow-lists (StrategyJson). The reader is the authority; this schema accepts nothing the reader refuses on the points it expresses, with one exception JSON Schema cannot state: a whole-number key must be written without a fraction or exponent (2, not 2.0). It is stricter than the reader in these places: an enum given as a number in a string, a time with surrounding spaces, and a non-array 'branches', branch 'entryConditions' or branch 'takeProfitTargets' (which the reader ignores). Rules it cannot state: every step's symbol equals the strategy's, and numbers must be finite.",
        };
        foreach (var (key, value) in root.ToList())
        {
            root.Remove(key);
            result[key] = value;
        }
        result["$defs"] = defs;
        return result;
    }

    // ── what each key holds ─────────────────────────────────────────────

    /// <summary>One key's value schema, whether the reader refuses a document without it, and its meaning.</summary>
    private sealed record KeySpec(Func<JsonObject> Value, string Description, bool Required = false);

    private static readonly Dictionary<string, KeySpec> DefinitionSpecs = new(StringComparer.Ordinal)
    {
        ["schemaVersion"] = new(() => new JsonObject { ["const"] = CurrentSchemaVersion }, "The format version; this build reads only this one.", Required: true),
        ["symbol"] = new(() => new JsonObject { ["type"] = "string", ["pattern"] = "\\S" }, "The ticker the strategy trades. Not empty.", Required: true),
        ["name"] = new(() => Text(), "A display name."),
        ["session"] = new(() => EnumOf<TradingSession>(), "The trading session it may act in (RTH when absent)."),
        ["quantity"] = new(() => Whole(), "Share count; 0 or absent means the workspace default size at fire time."),
        ["notionalAmount"] = new(() => Number(), "Dollar size instead of a share count."),
        ["direction"] = new(() => EnumOf<TradeDirection>(), "Long or Short (Long when absent)."),
        ["entryConditions"] = new(() => ListOf("condition"), "Every one must hold for the entry to fire (Filters and Entry conditions)."),
        ["takeProfitPrice"] = new(() => Number(), "Take profit at this price."),
        ["takeProfitPercent"] = new(() => Number(), "Take profit this percent from the entry price."),
        ["takeProfitTargets"] = new(() => ListOf("takeProfitTarget"), "Scale-out targets."),
        ["stopLossPrice"] = new(() => Number(), "Stop out at this price."),
        ["stopLossPercent"] = new(() => Number(), "Stop out this percent from the entry price."),
        ["trailingStopPercent"] = new(() => Number(), "Trailing stop, percent from the best price since entry."),
        ["exitTime"] = new(() => TimeOfDay(), "Flatten at this ET time of day."),
        ["peakGivebackPercent"] = new(() => Number(), "Exit after giving back this percent of the entry-to-peak run."),
        ["peakGivebackArmTime"] = new(() => TimeOfDay(), "The giveback exit is armed only from this ET time."),
        ["exitAtPriorHigh"] = new(() => Flag(), "Exit at the prior day's high."),
        ["rollingHighDays"] = new(() => Whole(), "Exit at the high of this many days."),
        ["rollingHighBuffer"] = new(() => Number(), "Buffer, in percent, for the rolling-high exit."),
        ["rollingLowDays"] = new(() => Whole(), "Exit at the low of this many days."),
        ["rollingLowBuffer"] = new(() => Number(), "Buffer, in percent, for the rolling-low exit."),
        ["entryRollingLowDays"] = new(() => Whole(), "Enter near the low of this many days."),
        ["entryRollingLowBuffer"] = new(() => Number(), "Buffer, in percent, for the rolling-low entry."),
        ["entryRollingHighDays"] = new(() => Whole(), "Enter near the high of this many days."),
        ["entryRollingHighBuffer"] = new(() => Number(), "Buffer, in percent, for the rolling-high entry."),
        ["isAutonomous"] = new(() => Flag(), "AutonomousTrading() was set."),
        ["isAdaptive"] = new(() => Flag(), "AdaptiveOrder() was set."),
        ["shouldRepeat"] = new(() => Flag(), "Re-arm after the position closes (Repeat)."),
        ["conditionalBlocks"] = new(() => ListOf("conditionalBlock"), "If/ElseIf/Else blocks."),
        ["maxCycles"] = new(() => Whole(), "The most times a repeating strategy may cycle."),
        ["whileConditions"] = new(() => ListOf("condition"), "While() conditions: the strategy acts only while every one holds."),
        ["exitWhenWhileFails"] = new(() => Flag(), "Exit the position when a While condition stops holding."),
        ["velocityExhaustionPctPerMin"] = new(() => Number(), "Velocity-exhaustion exit: sell once favorable velocity falls under this percent per minute."),
        ["velocityExhaustionWindowMinutes"] = new(() => new JsonObject { ["enum"] = new JsonArray(1, 3, 5, null) }, "The velocity window in minutes: 1, 3 or 5."),
        ["velocityExhaustionArmTime"] = new(() => TimeOfDay(), "The velocity exit is armed only from this ET time."),
        ["velocityExhaustionMinRunPercent"] = new(() => Number(), "The entry-to-peak run needed before the velocity exit may fire."),
        ["velocityExhaustionRequireLowerHigh"] = new(() => Flag(), "The velocity exit also needs a lower high since entry."),
        ["velocityExhaustionRequireVwapLoss"] = new(() => Flag(), "The velocity exit also needs price under the session VWAP."),
        ["velocityExhaustionExtensionSwitchPct"] = new(() => Number(), "Extension, in percent, past which the velocity exit switches mode."),
        ["stepName"] = new(() => Text(), "This step's name in a sequence."),
        ["thenWhen"] = new(() => ListOf("condition"), "The transition to the next step: every one must hold."),
        ["thenWithinDays"] = new(() => Whole(minimum: 1), "The transition must happen within this many days (at least 1)."),
        ["nextSteps"] = new(() => ListOf("step"), "The later steps of a sequence, in order. Only the strategy itself carries them; a step never nests steps."),
    };

    private static readonly Dictionary<string, string> ConditionDescriptions = new(StringComparer.Ordinal)
    {
        ["and"] = "Both conditions hold.",
        ["or"] = "Either condition holds.",
        ["not"] = "The inner condition does not hold.",
        ["indicator"] = "An indicator test; p1 and p2 are its parameters (a period, a threshold), as the verb that wrote it defines them.",
        ["pattern"] = "A price or candle pattern.",
        ["entryPrice"] = "Entry(price): price is at or through the entry price.",
        ["priceLevel"] = "Price against a fixed level.",
        ["timeWindow"] = "The ET time of day is inside the window.",
        ["priceBand"] = "Price is inside the band.",
        ["gapBand"] = "The gap percent is inside the band.",
    };

    private static readonly Dictionary<string, Dictionary<string, KeySpec>> ConditionSpecs = new(StringComparer.Ordinal)
    {
        ["and"] = new()
        {
            ["left"] = new(() => Ref("condition"), "The first condition.", Required: true),
            ["right"] = new(() => Ref("condition"), "The second condition.", Required: true),
        },
        ["or"] = new()
        {
            ["left"] = new(() => Ref("condition"), "The first condition.", Required: true),
            ["right"] = new(() => Ref("condition"), "The second condition.", Required: true),
        },
        ["not"] = new() { ["inner"] = new(() => Ref("condition"), "The condition that must not hold.", Required: true) },
        ["indicator"] = new()
        {
            ["indicator"] = new(() => EnumOf<IndicatorType>(nullable: false), "Which indicator test.", Required: true),
            ["p1"] = new(() => Number(), "First parameter."),
            ["p2"] = new(() => Number(), "Second parameter."),
            ["phase"] = new(() => EnumOf<StrategyPhase>(), "The phase the condition belongs to (Entry when absent)."),
        },
        ["pattern"] = new()
        {
            ["pattern"] = new(() => EnumOf<PatternType>(nullable: false), "Which pattern.", Required: true),
            ["level"] = new(() => Number(), "The price level the pattern is measured against, when it has one."),
        },
        ["entryPrice"] = new() { ["price"] = new(() => Number(nullable: false), "The entry price.", Required: true) },
        ["priceLevel"] = new()
        {
            ["kind"] = new(() => EnumOf<PriceLevelType>(nullable: false), "How price relates to the level.", Required: true),
            ["level"] = new(() => Number(nullable: false), "The price level.", Required: true),
            ["tolerancePercent"] = new(() => Number(), "How near counts as near, in percent (1 when absent)."),
        },
        ["timeWindow"] = new()
        {
            ["startEt"] = new(() => TimeOfDay(nullable: false), "Window start, ET.", Required: true),
            ["endEt"] = new(() => TimeOfDay(nullable: false), "Window end, ET.", Required: true),
        },
        ["priceBand"] = new()
        {
            ["min"] = new(() => Number(nullable: false), "Lowest price.", Required: true),
            ["max"] = new(() => Number(nullable: false), "Highest price.", Required: true),
        },
        ["gapBand"] = new()
        {
            ["minPercent"] = new(() => Number(nullable: false), "Smallest gap, percent.", Required: true),
            ["maxPercent"] = new(() => Number(nullable: false), "Largest gap, percent.", Required: true),
        },
    };

    private static readonly Dictionary<string, KeySpec> TargetSpecs = new(StringComparer.Ordinal)
    {
        ["label"] = new(() => Text(), "The target's label (T1 when absent)."),
        ["price"] = new(() => Number(nullable: false), "The target price.", Required: true),
        ["percentToSell"] = new(() => Whole(), "Percent of the position to sell there (100 when absent)."),
    };

    private static readonly Dictionary<string, KeySpec> BlockSpecs = new(StringComparer.Ordinal)
    {
        ["branches"] = new(() => ListOf("branch"), "The branches, in the order they are tried."),
    };

    private static readonly Dictionary<string, KeySpec> BranchSpecs = new(StringComparer.Ordinal)
    {
        ["condition"] = new(() => new JsonObject { ["anyOf"] = new JsonArray(Ref("condition"), new JsonObject { ["type"] = "null" }) },
            "When the branch is chosen; null or absent is the Else branch."),
        ["overrides"] = new(() => Ref("overrides"), "What the branch changes.", Required: true),
    };

    private static readonly Dictionary<string, KeySpec> OverridesSpecs = new(StringComparer.Ordinal)
    {
        ["direction"] = new(() => EnumOf<TradeDirection>(), "Long or Short for this branch."),
        ["entryConditions"] = new(() => ListOf("condition"), "Extra entry conditions for this branch."),
        ["takeProfitPrice"] = new(() => Number(), "Take profit at this price."),
        ["takeProfitTargets"] = new(() => ListOf("takeProfitTarget"), "Scale-out targets for this branch."),
        ["stopLossPrice"] = new(() => Number(), "Stop out at this price."),
        ["stopLossPercent"] = new(() => Number(), "Stop out this percent from the entry price."),
        ["trailingStopPercent"] = new(() => Number(), "Trailing stop percent."),
    };

    // ── schema fragments ────────────────────────────────────────────────

    private static JsonObject ObjectSchema(IEnumerable<string> keys, IReadOnlyDictionary<string, KeySpec> specs, string? description)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var key in keys)
        {
            if (specs.TryGetValue(key, out var spec))
            {
                var value = spec.Value();
                value["description"] = spec.Description;
                properties[key] = value;
                if (spec.Required) required.Add(key);
            }
            else
            {
                // The reader accepts the key but nobody has said what it holds; published honestly, and the contract test fails.
                properties[key] = new JsonObject { ["description"] = UndescribedKey };
            }
        }
        var schema = new JsonObject { ["type"] = "object" };
        if (description is not null) schema["description"] = description;
        schema["properties"] = properties;
        if (required.Count > 0) schema["required"] = required;
        schema["additionalProperties"] = false;
        return schema;
    }

    /// <summary>The description a key gets when the reader accepts it but the schema tables do not describe it yet.</summary>
    public const string UndescribedKey = "Accepted by the reader; its value is not described in the schema yet.";

    private static JsonObject Ref(string definition) => new() { ["$ref"] = "#/$defs/" + definition };

    private static JsonObject ListOf(string definition) => new() { ["type"] = "array", ["items"] = Ref(definition) };

    private static JsonObject Text() => new() { ["type"] = new JsonArray("string", "null") };

    private static JsonObject Flag() => new() { ["type"] = new JsonArray("boolean", "null") };

    private static JsonObject Number(bool nullable = true) =>
        new() { ["type"] = nullable ? new JsonArray("number", "null") : "number" };

    private static JsonObject Whole(int minimum = int.MinValue) => new()
    {
        ["type"] = new JsonArray("integer", "null"),
        ["minimum"] = minimum,
        ["maximum"] = int.MaxValue,
    };

    /// <summary>What <see cref="StrategyBuilder.ParseTimeOfDay"/> reads: H:mm or HH:mm, optionally with :ss, before 24:00.</summary>
    private static JsonObject TimeOfDay(bool nullable = true) => new()
    {
        ["type"] = nullable ? new JsonArray("string", "null") : "string",
        ["pattern"] = "^([01]?[0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9])?$",
    };

    /// <summary>The enum's member names, exactly as written (the reader matches case).</summary>
    private static JsonObject EnumOf<TEnum>(bool nullable = true) where TEnum : struct, Enum
    {
        var names = new JsonArray([.. Enum.GetNames<TEnum>().Select(n => (JsonNode)n)]);
        if (nullable) names.Add(null);
        return new JsonObject { ["enum"] = names };
    }
}
