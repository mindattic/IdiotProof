using IdiotProof.Blazor.Services;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Settings;

public sealed record SettingsListArgs
{
    [Option("all", Description = "Also show internal bookkeeping keys the app writes for itself.")]
    public bool All { get; init; }
}

/// <summary>
/// The runtime settings in <c>SettingsKv</c> (IP-T250): every key in <see cref="SettingsMetadata"/>
/// with its current value (or default), type, range and meaning, plus any stored key the registry
/// doesn't know. App-wide, not per user.
/// </summary>
[Operation("settings list", Summary = "List the app's runtime settings with current value, default, range and meaning.", Risk = OperationRisk.Read)]
[Example("idiotproof settings list")]
[Example("idiotproof settings list --all")]
public sealed class SettingsListOperation : IOperation<SettingsListArgs>
{
    public async Task<OperationResult> ExecuteAsync(SettingsListArgs args, OperationContext ctx)
    {
        var stored = await ctx.Get<SettingsRepository>().GetAllAsync(ctx.CancellationToken);
        var rows = new List<SettingRow>();
        foreach (var d in SettingsMetadata.All.Where(d => args.All || !d.Internal))
        {
            if (d.Key.EndsWith(".*", StringComparison.Ordinal))
            {
                foreach (var (k, v) in stored.Where(s => d.Matches(s.Key)).OrderBy(s => s.Key))
                    rows.Add(SettingRow.From(d, k, v));
                continue;
            }
            rows.Add(SettingRow.From(d, d.Key, stored.TryGetValue(d.Key, out var value) ? value : null));
        }
        var unknown = stored.Keys.Where(k => SettingsMetadata.Find(k) is null).OrderBy(k => k).ToList();
        if (args.All)
            rows.AddRange(unknown.Select(k => new SettingRow(k, stored[k], null, "?", "", "unregistered key", false, true)));

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("key").Column("value", maxWidth: 30).Column("default").Column("type").Column("range").Column("meaning", maxWidth: 60);
        foreach (var r in rows)
            table.Row(r.Key, r.Value ?? "(default)", r.Default ?? "-", r.Type + (r.Internal ? ", internal" : ""), r.Range, r.Meaning);

        var text = table.Render() + "\n\nChange one with `idiotproof settings set <key> <value>`.";
        if (!args.All && unknown.Count > 0) text += $" {unknown.Count} unregistered key(s) hidden; --all shows them.";
        return OperationResult.Ok(rows, text);
    }
}

public sealed record SettingsGetArgs
{
    [Argument(0, Name = "key", Description = "The setting key, e.g. Options.RiskFreeRate.")]
    public string Key { get; init; } = "";
}

/// <summary>One runtime setting: its stored value (or default) and what it means (IP-T250).</summary>
[Operation("settings get", Summary = "Show one runtime setting: value, default, range and meaning.", Risk = OperationRisk.Read)]
[Example("idiotproof settings get Options.RiskFreeRate")]
public sealed class SettingsGetOperation : IOperation<SettingsGetArgs>
{
    public async Task<OperationResult> ExecuteAsync(SettingsGetArgs args, OperationContext ctx)
    {
        var key = args.Key.Trim();
        var value = await ctx.Get<SettingsRepository>().GetAsync(key, ctx.CancellationToken);
        var d = SettingsMetadata.Find(key);
        if (d is null && value is null)
            return OperationResult.Usage($"Unknown setting '{key}'. `idiotproof settings list` shows them.");

        var row = d is null
            ? new SettingRow(key, value, null, "?", "", "unregistered key", false, true)
            : SettingRow.From(d, key, value);
        var block = new KeyValueBlock()
            .Add("Key", row.Key)
            .Add("Value", row.Value ?? $"(default) {row.Default ?? "-"}")
            .Add("Default", row.Default)
            .Add("Type", row.Type + (row.Internal ? " (internal: written by the app)" : ""))
            .Add("Range", row.Range.Length == 0 ? null : row.Range)
            .Add("Meaning", row.Meaning);
        return OperationResult.Ok(row, block.Render());
    }
}

public sealed record SettingsSetArgs
{
    [Argument(0, Name = "key", Description = "The setting key (see `settings list`).")]
    public string Key { get; init; } = "";

    [Argument(1, Name = "value", Description = "The new value; checked against the key's type and range.")]
    public string Value { get; init; } = "";
}

/// <summary>
/// Changes one runtime setting after checking it against <see cref="SettingsMetadata"/> (IP-T250):
/// unknown keys and internal bookkeeping keys are refused, values are type- and range-checked and
/// stored normalized (invariant culture). Readers pick it up on their next read.
/// </summary>
[Operation("settings set", Summary = "Change one runtime setting (type- and range-checked; unknown and internal keys refused).", Risk = OperationRisk.Write)]
[Example("idiotproof settings set Options.RiskFreeRate 0.045")]
[Example("idiotproof settings set decay.be-bex.threshold 10")]
public sealed class SettingsSetOperation : IOperation<SettingsSetArgs>
{
    public async Task<OperationResult> ExecuteAsync(SettingsSetArgs args, OperationContext ctx)
    {
        var key = args.Key.Trim();
        var d = SettingsMetadata.Find(key);
        if (d is null)
            return OperationResult.Usage($"Unknown setting '{key}'. Settable: {string.Join(", ", SettingsMetadata.All.Where(x => !x.Internal).Select(x => x.Key))}.");
        if (d.Internal)
            return OperationResult.Blocked($"{key} is internal bookkeeping the app writes for itself; it can't be set by hand.");
        var (value, error) = d.Validate(args.Value);
        if (error is not null) return OperationResult.Usage(error);

        var repo = ctx.Get<SettingsRepository>();
        var was = await repo.GetAsync(d.Key, ctx.CancellationToken);
        await repo.SetAsync(d.Key, value!, ctx.CancellationToken);
        return OperationResult.Ok(new { key = d.Key, was, value, changed = was != value },
            $"{d.Key}: {was ?? $"(default {d.Default ?? "-"})"} -> {value}.");
    }
}

/// <summary>A setting as shown (JSON: key, value, default, type, range, meaning, internal, unregistered).</summary>
public sealed record SettingRow(string Key, string? Value, string? Default, string Type, string Range, string Meaning, bool Internal, bool Unregistered)
{
    public static SettingRow From(SettingDefinition d, string key, string? value) => new(
        key, value, d.Default, d.Kind.ToString().ToLowerInvariant(),
        d.Min is null && d.Max is null ? "" : $"{d.Min?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""}..{d.Max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""}",
        d.Description, d.Internal, false);
}
