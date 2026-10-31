using System.Globalization;
using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using IdiotProof.Core.Ledger;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Settings;

/// <summary>The per-user preferences a person may read and change from the command line.</summary>
internal static class PrefFields
{
    public sealed record Field(string Name, string Meaning, Func<UserPreferences, string> Read);

    public static readonly Field[] All =
    [
        new("theme", "Web app theme", p => p.Theme),
        new("account-type", "Which account pill the web app opens on (paper or live)", p => p.ActiveAccountType),
        new("active-account", "The selected Alpaca account (masked)", p => string.IsNullOrEmpty(p.ActiveAccountId) ? "-" : SecretRedactor.MaskAccountNumber(p.ActiveAccountId)),
        new("risk.max-loss-per-trade", "Risk Guardian: largest loss one trade may risk ($)", p => Num(p.RiskMaxLossPerTrade)),
        new("risk.max-loss-per-day", "Risk Guardian: daily loss circuit breaker ($, at least the per-trade cap)", p => Num(p.RiskMaxLossPerDay)),
        new("risk.min-stop-percent", "Risk Guardian: tightest stop allowed, % of entry", p => Num(p.RiskMinStopLossPercent)),
        new("risk.max-stop-percent", "Risk Guardian: widest stop allowed, % of entry", p => Num(p.RiskMaxStopLossPercent)),
        new("risk.account-balance", "Risk Guardian: balance position sizes are computed against ($)", p => Num(p.RiskAccountBalance)),
        new("risk.max-account-risk-percent", "Risk Guardian: most of the account one trade may risk (%)", p => Num(p.RiskMaxAccountRiskPercent)),
    ];

    public static Field? Find(string name) =>
        All.FirstOrDefault(f => string.Equals(f.Name, name.Trim().Replace('_', '-'), StringComparison.OrdinalIgnoreCase));

    public static string Num(decimal d) => d.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed record PrefsGetArgs
{
    [Argument(0, Name = "field", Required = false, Description = "One preference (theme, account-type, risk.max-loss-per-trade, ...); omit for all.")]
    public string? Field { get; init; }
}

/// <summary>Your per-user preferences (IP-T251): theme, account pill and the Risk Guardian limits.</summary>
[Operation("prefs get", Summary = "Show your preferences: theme, account pill and Risk Guardian limits.", Risk = OperationRisk.Read)]
[Example("idiotproof prefs get")]
[Example("idiotproof prefs get risk.max-loss-per-day")]
public sealed class PrefsGetOperation : IOperation<PrefsGetArgs>
{
    public async Task<OperationResult> ExecuteAsync(PrefsGetArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var p = await ctx.Get<UserPreferencesService>().GetOrCreateAsync(owner, ctx.CancellationToken);
        var fields = PrefFields.All.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(args.Field))
        {
            var f = PrefFields.Find(args.Field) ?? throw OperationException.Usage(
                $"Unknown preference '{args.Field}'. One of: {string.Join(", ", PrefFields.All.Select(x => x.Name))}.");
            fields = [f];
        }

        var data = fields.ToDictionary(f => f.Name, f => f.Read(p));
        var table = new TextTable { MaxWidth = ctx.Width }.Column("preference").Column("value", Align.Right).Column("meaning", maxWidth: 70);
        foreach (var f in fields) table.Row(f.Name, f.Read(p), f.Meaning);
        return OperationResult.Ok(data, table.Render() + (args.Field is null ? $"\n\nUpdated {ctx.Et(p.UpdatedUtc)}." : ""));
    }
}

public sealed record PrefsSetArgs
{
    [Argument(0, Name = "field", Description = "theme, account-type or a risk.* limit (see `prefs get`).")]
    public string Field { get; init; } = "";

    [Argument(1, Name = "value", Description = "The new value.")]
    public string Value { get; init; } = "";
}

/// <summary>
/// Changes one of your preferences (IP-T251). Risk limits go through the same sanity rules as the
/// Settings page (daily cap at least the per-trade cap, max stop at least min stop, percents
/// clamped), and the result reports what was actually stored.
/// </summary>
[Operation("prefs set", Summary = "Change one of your preferences (risk limits keep the Settings page's sanity rules).", Risk = OperationRisk.Write)]
[Example("idiotproof prefs set risk.max-loss-per-day 250")]
[Example("idiotproof prefs set account-type paper")]
public sealed class PrefsSetOperation : IOperation<PrefsSetArgs>
{
    public async Task<OperationResult> ExecuteAsync(PrefsSetArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var field = PrefFields.Find(args.Field);
        if (field is null)
            return OperationResult.Usage($"Unknown preference '{args.Field}'. One of: {string.Join(", ", PrefFields.All.Select(x => x.Name))}.");
        var service = ctx.Get<UserPreferencesService>();
        var p = await service.GetOrCreateAsync(owner, ctx.CancellationToken);
        var was = field.Read(p);
        var raw = args.Value.Trim();

        switch (field.Name)
        {
            case "theme":
                if (raw.Length is 0 or > 64) return OperationResult.Usage("theme takes a name of 1 to 64 characters.");
                await service.SetThemeAsync(owner, raw, ctx.CancellationToken);
                break;
            case "account-type":
                var type = raw.ToLowerInvariant();
                if (type is not ("paper" or "live")) return OperationResult.Usage("account-type takes paper or live.");
                await service.SetActiveAccountAsync(owner, p.ActiveAccountId, type, ctx.CancellationToken);
                break;
            case "active-account":
                return OperationResult.Usage("active-account follows the account you pick in the web app; it can't be set here.");
            default:
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
                    return OperationResult.Usage($"{field.Name} takes a number.");
                await service.SetRiskConfigAsync(owner,
                    field.Name == "risk.max-loss-per-trade" ? v : p.RiskMaxLossPerTrade,
                    field.Name == "risk.max-loss-per-day" ? v : p.RiskMaxLossPerDay,
                    field.Name == "risk.min-stop-percent" ? v : p.RiskMinStopLossPercent,
                    field.Name == "risk.max-stop-percent" ? v : p.RiskMaxStopLossPercent,
                    field.Name == "risk.account-balance" ? v : p.RiskAccountBalance,
                    field.Name == "risk.max-account-risk-percent" ? v : p.RiskMaxAccountRiskPercent,
                    ctx.CancellationToken);
                break;
        }

        var now = field.Read(await service.GetOrCreateAsync(owner, ctx.CancellationToken));
        var adjusted = field.Name.StartsWith("risk.", StringComparison.Ordinal) && now != PrefFields.Num(decimal.Parse(raw, CultureInfo.InvariantCulture));
        return OperationResult.Ok(new { field = field.Name, was, value = now, adjusted },
            $"{field.Name}: {was} -> {now}" + (adjusted ? " (adjusted by the Risk Guardian sanity rules)." : "."));
    }
}
