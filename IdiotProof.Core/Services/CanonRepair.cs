using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Core.Data;
using IdiotProof.Scripting;

namespace IdiotProof.Core.Services;

/// <summary>
/// Whether the Monitor may repair quarantined strategies by itself (IP-T525). Two SettingsKv
/// switches: <see cref="EnabledKey"/> (on unless set to false) covers Paper and Sandbox strategies;
/// <see cref="LiveKey"/> (off unless set to true) extends it to Live ones, whose canon otherwise
/// never changes without the owner seeing it.
/// </summary>
public sealed record CanonRepairPolicy(bool Enabled, bool Live)
{
    public const string EnabledKey = "quarantine.autorepair.enabled";
    public const string LiveKey = "quarantine.autorepair.live";

    /// <summary>What applies when neither key was ever set.</summary>
    public static readonly CanonRepairPolicy Default = new(Enabled: true, Live: false);

    public static async Task<CanonRepairPolicy> ReadAsync(SettingsRepository settings, CancellationToken ct = default) =>
        new(await settings.GetBoolAsync(EnabledKey, ct) ?? Default.Enabled,
            await settings.GetBoolAsync(LiveKey, ct) ?? Default.Live);

    /// <summary>Anything that is not plainly Paper or Sandbox is treated as Live.</summary>
    public static bool RoutesLive(Strategy row) =>
        !string.Equals(row.BrokerMode?.Trim(), "Paper", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(row.BrokerMode?.Trim(), "Sandbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>Why this policy forbids repairing <paramref name="row"/>, or null when it allows it.</summary>
    public string? Blocker(Strategy row)
    {
        if (!Enabled) return $"automatic repair is switched off ({EnabledKey} = false)";
        if (RoutesLive(row) && !Live)
            return $"it routes to Live, and a Live strategy's canon is never rewritten automatically ({LiveKey} = false)";
        return null;
    }
}

/// <summary>
/// What a canon repair of one quarantined strategy would do.
/// <para><c>Reason</c>: why the strategy is quarantined (the strict reader's message).</para>
/// <para><c>ProposedCanon</c>: the canon re-derived from the stored script, when one could be derived
/// and the strict reader accepts it; null otherwise.</para>
/// <para><c>Changes</c>: what the proposed canon has that the stored one does not, in words.</para>
/// <para><c>ContentBlocker</c>: why the script or the re-derived canon is not safe to write
/// (it does not change until the script or the canon does).</para>
/// <para><c>StateBlocker</c>: why the row may not be touched right now (holding, mid-sequence).</para>
/// </summary>
public sealed record CanonRepairPlan(
    string Reason, string? ProposedCanon, IReadOnlyList<string> Changes, string? ContentBlocker, string? StateBlocker)
{
    /// <summary>True when the repair loses nothing and the row is in a state that may be rewritten.</summary>
    public bool Safe => ProposedCanon is not null && ContentBlocker is null && StateBlocker is null;

    public string? Blocker => StateBlocker ?? ContentBlocker;
}

/// <summary>
/// The rule for repairing a quarantined strategy's canon from its own stored script (IP-LAW-8,
/// IP-T525). Pure: it reads one row and writes nothing; <see cref="StrategyRepository"/> owns the
/// write. Failing closed is the rule and repair the exception, so every doubt is a blocker.
/// <para>A repair is safe only when all of this holds:</para>
/// <list type="bullet">
/// <item>the row has a canon and the strict reader rejects it (a row with no canon is not repaired);</item>
/// <item>the stored script has no error diagnostics (<see cref="ScriptDiagnostics"/>) and parses;</item>
/// <item>the re-derived canon passes the strict reader and writes back byte for byte;</item>
/// <item>the re-derived canon is for the row's symbol;</item>
/// <item>the stored canon is a JSON object of this build's schema version, so what it expressed can be counted;</item>
/// <item>step by step, the re-derived canon has no fewer entry, While or ThenWhen conditions,
/// conditional blocks, branches, branch conditions, branch exits or take-profit targets, every
/// exit rule and entry gate the stored canon set, and no fewer steps;</item>
/// <item>step by step, session, direction and order size are what the stored canon said;</item>
/// <item>the row is flat (no position) and not part-way through a sequence.</item>
/// </list>
/// </summary>
public static class CanonRepair
{
    /// <summary>AuditLog category of a completed repair.</summary>
    public const string AuditCategory = "strategy-repaired";

    private static readonly string[] ExitKeys =
    [
        "takeProfitPrice", "takeProfitPercent", "stopLossPrice", "stopLossPercent", "trailingStopPercent",
        "exitTime", "peakGivebackPercent", "exitAtPriorHigh", "rollingHighDays", "rollingLowDays",
        "exitWhenWhileFails", "velocityExhaustionPctPerMin",
    ];

    private static readonly string[] GateKeys = ["entryRollingLowDays", "entryRollingHighDays"];

    private static readonly string[] CompositeKeys = ["left", "right", "inner"];

    private static readonly string[] BranchExitKeys =["takeProfitPrice", "stopLossPrice", "stopLossPercent", "trailingStopPercent"];

    /// <summary>
    /// The repair plan for <paramref name="row"/>, or null when the row is not quarantined (its
    /// canon reads, or it has no canon and its script parses).
    /// </summary>
    public static CanonRepairPlan? Plan(Strategy row)
    {
        StrategyLoadResult loaded;
        try { loaded = StrategyLoader.Load(row.ScriptJson, row.ScriptText); }
        catch (Exception ex) { loaded = new StrategyLoadResult(null, false, $"the canon could not be read: {ex.Message}"); }
        if (loaded.Definition is not null) return null;

        var state = StateBlocker(row);
        if (loaded.CanonicalError is not { } reason)
            return new CanonRepairPlan("no canon, and the script doesn't parse", null, [],
                "there is no canon and the script does not parse, so there is nothing to derive a canon from", state);

        if (string.IsNullOrWhiteSpace(row.ScriptText))
            return new CanonRepairPlan(reason, null, [], "the strategy has no script text to derive a canon from", state);

        string? proposed;
        string? content;
        try
        {
            content = Derive(row, out proposed);
        }
        catch (Exception ex)
        {
            proposed = null;
            content = $"the script could not be checked ({ex.GetType().Name}: {ex.Message})";
        }
        if (proposed is null) return new CanonRepairPlan(reason, null, [], content, state);

        var changes = new List<string>();
        try
        {
            content ??= Compare(row.ScriptJson!, proposed, changes);
        }
        catch (Exception ex)
        {
            content = $"the stored and re-derived canons could not be compared ({ex.GetType().Name}: {ex.Message})";
        }
        return new CanonRepairPlan(reason, proposed, changes, content, state);
    }

    /// <summary>Why the row's runtime state forbids a rewrite, or null when it is flat and at step 1.</summary>
    public static string? StateBlocker(Strategy row)
    {
        if (row.PositionQty != 0) return $"it holds a position ({row.PositionQty} shares); flatten it first";
        if (row.StepIndex != 0 || row.AwaitingTransition)
            return "it is part-way through its sequence; reset it to step 1 first (`idiotproof strategy step --reset`)";
        return null;
    }

    /// <summary>Script → canon. Returns the content blocker (null when none) and the canon when the strict reader accepts it.</summary>
    private static string? Derive(Strategy row, out string? proposed)
    {
        proposed = null;
        var errors = ScriptDiagnostics.Analyze(row.ScriptText).Where(d => d.IsError).ToList();
        if (errors.Count > 0)
        {
            var first = errors[0];
            return $"the script has {errors.Count} error(s); first: line {first.Line}:{first.Column} {first.Code} {first.Message}";
        }

        var def = ScriptParser.ParseScript(row.ScriptText);
        if (def is null) return "the script does not parse";

        var canon = StrategyJson.Serialize(def);
        StrategyDefinition reread;
        try { reread = StrategyJson.Deserialize(canon); }
        catch (StrategyJsonException ex) { return $"the strict reader rejects the re-derived canon: {ex.Message}"; }
        if (!string.Equals(StrategyJson.Serialize(reread), canon, StringComparison.Ordinal))
            return "the re-derived canon does not read back to the same canon";

        // From here the canon is one the Monitor could run; the remaining checks decide whether
        // writing it would lose or change anything the stored canon said.
        proposed = canon;
        if (!string.Equals(reread.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase))
            return $"the script is for {reread.Symbol}, but the strategy is for {row.Symbol}";
        return null;
    }

    /// <summary>Stored canon vs re-derived canon: the losses as one blocker (null when none), the gains into <paramref name="changes"/>.</summary>
    private static string? Compare(string storedCanon, string proposedCanon, List<string> changes)
    {
        JsonDocument stored;
        try { stored = JsonDocument.Parse(storedCanon); }
        catch (JsonException) { return "the stored canon is not JSON, so what it expressed cannot be compared with the script"; }

        using (stored)
        using (var proposed = JsonDocument.Parse(proposedCanon))
        {
            if (stored.RootElement.ValueKind != JsonValueKind.Object)
                return "the stored canon is not a JSON object, so what it expressed cannot be compared with the script";
            // Counting reads the stored canon by this schema's key names; another schema's canon
            // could hold conditions under names this build does not know to count.
            if (!stored.RootElement.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var schema) || schema != StrategyJson.CurrentSchemaVersion)
                return $"the stored canon is not schema version {StrategyJson.CurrentSchemaVersion}, so what it expressed cannot be counted against the script";

            var oldSteps = Steps(stored.RootElement);
            var newSteps = Steps(proposed.RootElement);
            var losses = new List<string>();
            if (newSteps.Count < oldSteps.Count) losses.Add($"steps {oldSteps.Count} → {newSteps.Count}");
            else if (newSteps.Count > oldSteps.Count) changes.Add($"steps {oldSteps.Count} → {newSteps.Count}");

            for (var i = 0; i < Math.Min(oldSteps.Count, newSteps.Count); i++)
            {
                var label = oldSteps.Count > 1 || newSteps.Count > 1 ? $"step {i + 1} " : "";
                CompareStep(label, oldSteps[i], newSteps[i], losses, changes);
            }

            if (losses.Count > 0) return "the re-derived canon would lose or change: " + string.Join("; ", losses);
            if (changes.Count == 0) changes.Add("same conditions, branches, exits and steps; rewritten in the form this build reads");
            return null;
        }
    }

    private static List<JsonElement> Steps(JsonElement root)
    {
        var steps = new List<JsonElement> { root };
        if (root.TryGetProperty("nextSteps", out var next) && next.ValueKind == JsonValueKind.Array)
            steps.AddRange(next.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object));
        return steps;
    }

    private static void CompareStep(string label, JsonElement before, JsonElement after, List<string> losses, List<string> changes)
    {
        void Count(string what, int was, int now)
        {
            if (now < was) losses.Add($"{label}{what} {was} → {now}");
            else if (now > was) changes.Add($"{label}{what} {was} → {now}");
        }

        Count("entry conditions", Leaves(before, "entryConditions"), Leaves(after, "entryConditions"));
        Count("While conditions", Leaves(before, "whileConditions"), Leaves(after, "whileConditions"));
        Count("ThenWhen conditions", Leaves(before, "thenWhen"), Leaves(after, "thenWhen"));
        Count("take-profit targets", ArrayLength(before, "takeProfitTargets"), ArrayLength(after, "takeProfitTargets"));

        var (blocksBefore, branchesBefore, conditionsBefore, exitsBefore) = Branching(before);
        var (blocksAfter, branchesAfter, conditionsAfter, exitsAfter) = Branching(after);
        Count("conditional blocks", blocksBefore, blocksAfter);
        Count("branches", branchesBefore, branchesAfter);
        Count("branch conditions", conditionsBefore, conditionsAfter);
        Count("branch exits", exitsBefore, exitsAfter);

        foreach (var key in ExitKeys.Concat(GateKeys))
        {
            bool was = IsSet(before, key), now = IsSet(after, key);
            if (was && !now) losses.Add($"{label}{key} dropped");
            else if (!was && now) changes.Add($"{label}{key} added");
        }

        // What it trades, which way and how much must be exactly what the stored canon said.
        Same(label, "session", Text(before, "session", "RTH"), Text(after, "session", "RTH"), losses);
        Same(label, "direction", Text(before, "direction", "Long"), Text(after, "direction", "Long"), losses);
        Same(label, "quantity", Number(before, "quantity", "0"), Number(after, "quantity", "0"), losses);
        Same(label, "notionalAmount", Number(before, "notionalAmount", "none"), Number(after, "notionalAmount", "none"), losses);
    }

    /// <summary>Compares one scalar; a stored value of the wrong kind is unreadable (null) and not compared.</summary>
    private static void Same(string label, string what, string? was, string? now, List<string> losses)
    {
        if (was is null) return;
        if (!string.Equals(was, now, StringComparison.Ordinal)) losses.Add($"{label}{what} {was} → {now ?? "?"}");
    }

    private static string? Text(JsonElement step, string key, string fallback)
    {
        if (!step.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return fallback;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string? Number(JsonElement step, string key, string fallback)
    {
        if (!step.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return fallback;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d.ToString("G29", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>A rule counts as set when its key holds anything but null or false.</summary>
    private static bool IsSet(JsonElement step, string key) =>
        step.TryGetProperty(key, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False);

    private static int ArrayLength(JsonElement step, string key) =>
        step.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;

    private static int Leaves(JsonElement step, string key) =>
        step.TryGetProperty(key, out var v) ? Leaves(v) : 0;

    /// <summary>Leaf conditions under a condition, a list of them, or nothing: and/or/not nodes count their children.</summary>
    private static int Leaves(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array) return el.EnumerateArray().Sum(Leaves);
        if (el.ValueKind != JsonValueKind.Object) return el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? 0 : 1;
        var composite = false;
        var children = 0;
        foreach (var name in CompositeKeys)
        {
            if (!el.TryGetProperty(name, out var child) || child.ValueKind != JsonValueKind.Object) continue;
            composite = true;
            children += Leaves(child);
        }
        return composite ? children : 1;
    }

    private static (int Blocks, int Branches, int Conditions, int Exits) Branching(JsonElement step)
    {
        int blocks = 0, branches = 0, conditions = 0, exits = 0;
        if (!step.TryGetProperty("conditionalBlocks", out var list) || list.ValueKind != JsonValueKind.Array) return (0, 0, 0, 0);
        foreach (var block in list.EnumerateArray())
        {
            blocks++;
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("branches", out var bs) || bs.ValueKind != JsonValueKind.Array) continue;
            foreach (var branch in bs.EnumerateArray())
            {
                branches++;
                if (branch.ValueKind != JsonValueKind.Object) continue;
                conditions += Leaves(branch, "condition");
                if (!branch.TryGetProperty("overrides", out var o) || o.ValueKind != JsonValueKind.Object) continue;
                conditions += Leaves(o, "entryConditions");
                exits += BranchExitKeys.Count(k => IsSet(o, k)) + ArrayLength(o, "takeProfitTargets");
                if (IsSet(o, "direction")) exits++; // a branch that flips direction is part of what it does
            }
        }
        return (blocks, branches, conditions, exits);
    }

    /// <summary>
    /// The audit payload of a repair: who did it, why the strategy was quarantined, and the canon
    /// before and after in full.
    /// </summary>
    public static string AuditData(Strategy row, CanonRepairPlan plan, string repairedBy) =>
        new JsonObject
        {
            ["strategyId"] = row.Id.ToString(),
            ["symbol"] = row.Symbol,
            ["title"] = row.Title,
            ["brokerMode"] = row.BrokerMode,
            ["repairedBy"] = repairedBy,
            ["quarantineReason"] = plan.Reason,
            ["changes"] = new JsonArray(plan.Changes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["canonBefore"] = row.ScriptJson,
            ["canonAfter"] = plan.ProposedCanon,
        }.ToJsonString();

    /// <summary>
    /// Line diff of the stored canon against the proposed one ("- " removed, "+ " added), at most
    /// <paramref name="maxLines"/> lines. A stored canon that is JSON is re-indented first so the
    /// two line up; one that is not JSON is shown as it is.
    /// </summary>
    public static IReadOnlyList<string> Diff(string? storedCanon, string proposedCanon, int maxLines = 60)
    {
        var before = Lines(Pretty(storedCanon ?? ""));
        var after = Lines(proposedCanon);

        // Longest common subsequence over lines; canons are a few hundred lines at most.
        var lcs = new int[before.Length + 1, after.Length + 1];
        for (var i = before.Length - 1; i >= 0; i--)
        for (var j = after.Length - 1; j >= 0; j--)
            lcs[i, j] = before[i] == after[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var diff = new List<string>();
        int a = 0, b = 0;
        while (a < before.Length || b < after.Length)
        {
            if (a < before.Length && b < after.Length && before[a] == after[b]) { a++; b++; }
            else if (b < after.Length && (a == before.Length || lcs[a, b + 1] > lcs[a + 1, b])) diff.Add("+ " + after[b++]);
            else diff.Add("- " + before[a++]);
        }
        if (diff.Count <= maxLines) return diff;
        var more = diff.Count - maxLines;
        var cut = diff.Take(maxLines).ToList();
        cut.Add($"... {more} more changed line(s)");
        return cut;
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToArray();

    private static string Pretty(string canon)
    {
        try { return JsonNode.Parse(canon)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? canon; }
        catch (Exception) { return canon; } // not JSON, or JSON the node model refuses (duplicate keys)
    }
}
