using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Account;

/// <summary>
/// The Alpaca account configuration keys (<c>/v2/account/configurations</c>), what each accepts and
/// what it means. Booleans go out as JSON booleans, <c>max_options_trading_level</c> as a number,
/// the rest as strings, matching what Alpaca returns.
/// </summary>
internal static class AccountConfigKeys
{
    public sealed record Key(string Name, string[] Values, string Meaning, Func<string, object> ToJson);

    private static readonly string[] Bools = ["true", "false"];

    public static readonly Key[] All =
    [
        new("dtbp_check", ["both", "entry", "exit"], "When the day-trading buying power check applies", v => v),
        new("pdt_check", ["both", "entry", "exit"], "When the pattern-day-trader check applies", v => v),
        new("trade_confirm_email", ["all", "none"], "Email a confirmation for every trade", v => v),
        new("suspend_trade", Bools, "Block all new orders", v => v == "true"),
        new("no_shorting", Bools, "Refuse orders that would open a short", v => v == "true"),
        new("fractional_trading", Bools, "Allow fractional-share orders", v => v == "true"),
        new("max_margin_multiplier", ["1", "2", "4"], "Margin cap: 1 = cash only, 2 = Reg T, 4 = day-trading margin", v => v),
        new("max_options_trading_level", ["0", "1", "2", "3"], "Highest options level to allow (0 off .. 3 spreads)", v => int.Parse(v, CultureInfo.InvariantCulture)),
        new("ptp_no_exception_entry", Bools, "Allow buying publicly traded partnerships without the 10% withholding exception", v => v == "true"),
    ];

    public static Key? Find(string name) =>
        All.FirstOrDefault(k => string.Equals(k.Name, name.Trim().Replace('-', '_'), StringComparison.OrdinalIgnoreCase));

    /// <summary>A value as text, the way the table shows it (JSON booleans lower-case).</summary>
    public static string Show(JsonElement config, string key) =>
        config.ValueKind == JsonValueKind.Object && config.TryGetProperty(key, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Null => "-",
                _ => v.GetRawText(),
            }
            : "-";
}

/// <summary>
/// Alpaca's per-account trading switches: day-trading and PDT checks, shorting, fractional shares,
/// margin cap, options level, trade-confirm email and the suspend-trading kill switch.
/// </summary>
[Operation("account config", Summary = "Alpaca account settings side by side: DTBP/PDT checks, shorting, fractional, margin cap, options level, trade suspension.", Risk = OperationRisk.Read)]
[Example("idiotproof account config")]
[Example("idiotproof account config --mode live")]
public sealed class AccountConfigOperation : IOperation<AccountScopeArgs>
{
    public async Task<OperationResult> ExecuteAsync(AccountScopeArgs args, OperationContext ctx)
    {
        var reads = await AccountApis.ForEachAsync(ctx, args.Mode, api => api.GetAsync("/v2/account/configurations", ct: ctx.CancellationToken));
        var table = new TextTable { MaxWidth = ctx.Width }.Column("Setting");
        foreach (var r in reads) table.Column(r.Name);
        table.Column("Meaning");
        foreach (var key in AccountConfigKeys.All)
            table.Row([key.Name, .. reads.Select(r => r.Error is null ? AccountConfigKeys.Show(r.Value, key.Name) : "?"), key.Meaning]);

        var blocks = new List<string> { table.Render() };
        blocks.AddRange(reads.Where(r => r.Error is not null).Select(r => $"{r.Name} account: {r.Error}"));
        blocks.Add("Change one with `idiotproof account config set <setting> <value> [--mode paper|live]`.");
        var data = reads.Select(r => r.Error is null
            ? (object)new { account = r.Key, configuration = r.Value }
            : new { account = r.Key, error = r.Error }).ToList();
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}

public sealed record AccountConfigSetArgs
{
    [Argument(0, Name = "setting", Description = "dtbp_check, pdt_check, trade_confirm_email, suspend_trade, no_shorting, fractional_trading, max_margin_multiplier, max_options_trading_level or ptp_no_exception_entry.")]
    public string Setting { get; init; } = "";

    [Argument(1, Name = "value", Description = "The new value (see `account config` for each setting's meaning).")]
    public string Value { get; init; } = "";

    [Option("mode", Description = "paper or live (default paper; live needs a confirmation code).")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

/// <summary>
/// Changes one Alpaca account setting (<c>PATCH /v2/account/configurations</c>). On Live the first
/// call previews the change and issues a confirmation code; the same call with <c>--confirm</c>
/// applies it, once, within the code's lifetime.
/// </summary>
[Operation("account config set", Summary = "Change one Alpaca account setting (Live needs a confirmation code).", Risk = OperationRisk.Write)]
[Example("idiotproof account config set no_shorting true")]
[Example("idiotproof account config set max_margin_multiplier 1 --mode live")]
public sealed class AccountConfigSetOperation : IOperation<AccountConfigSetArgs>
{
    /// <summary>The <see cref="ConfirmationStore"/> kind for a Live account-setting change.</summary>
    public const string ConfirmationKind = "account-config";

    public async Task<OperationResult> ExecuteAsync(AccountConfigSetArgs args, OperationContext ctx)
    {
        var key = AccountConfigKeys.Find(args.Setting);
        if (key is null)
            return OperationResult.Usage($"Unknown setting '{args.Setting}'. One of: {string.Join(", ", AccountConfigKeys.All.Select(k => k.Name))}.");
        var value = args.Value.Trim().ToLowerInvariant();
        if (!key.Values.Contains(value))
            return OperationResult.Usage($"{key.Name} takes {string.Join(", ", key.Values)}.");

        var owner = ctx.RequireOwner();
        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, args.Mode, ctx.CancellationToken);
        var before = await api.GetAsync("/v2/account/configurations", ct: ctx.CancellationToken);
        var was = AccountConfigKeys.Show(before, key.Name);
        var account = args.Mode.ToString().ToLowerInvariant();
        if (was == value)
            return OperationResult.Ok(new { account, setting = key.Name, value, changed = false }, $"{key.Name} is already {value} on {account}.");

        if (args.Mode == BrokerMode.Live)
        {
            var gate = await RequireCodeAsync(ctx, owner, key.Name, value,
                $"SET LIVE account {key.Name}: {was} -> {value} ({key.Meaning.ToLowerInvariant()}).");
            if (gate is not null) return gate;
        }

        var after = await api.SendAsync(HttpMethod.Patch, "/v2/account/configurations",
            new Dictionary<string, object?> { [key.Name] = key.ToJson(value) }, ct: ctx.CancellationToken);
        var now = AccountConfigKeys.Show(after, key.Name);
        return OperationResult.Ok(new { account, setting = key.Name, was, value = now, changed = true, configuration = after },
            $"{account} {key.Name}: {was} -> {now}.");
    }

    /// <summary>Null when the caller may proceed (a valid code for exactly this change), else the result to return.</summary>
    private static async Task<OperationResult?> RequireCodeAsync(OperationContext ctx, Guid owner, string setting, string value, string preview)
    {
        var payload = JsonSerializer.Serialize(new { account = "live", setting, value });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var pending = await store.IssueAsync(owner, ConfirmationKind, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, new { account = "live", setting, value });
        }
        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationKind && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }
}
