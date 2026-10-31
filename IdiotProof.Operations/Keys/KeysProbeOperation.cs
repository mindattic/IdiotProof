using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Ledger;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Keys;

public sealed record KeysProbeArgs
{
    [Option("mode", Description = "all (default), paper or live.")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

/// <summary>
/// Proves the owner's stored Alpaca pairs still work (IP-T249): one read-only
/// <c>GET /v2/account</c> per account against its own host, timed. Reports the account (number
/// masked to its last four), status and whether trading is blocked. Works before the routing
/// opt-in (it only reads), and never shows a key.
/// </summary>
[Operation("keys probe", Summary = "Round-trip your stored Alpaca keys with a read-only account call (paper, live or both).", Risk = OperationRisk.Read)]
[Example("idiotproof keys probe")]
[Example("idiotproof keys probe --mode live")]
public sealed class KeysProbeOperation : IOperation<KeysProbeArgs>
{
    public async Task<OperationResult> ExecuteAsync(KeysProbeArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var keys = await ctx.Get<UserKeyService>().GetOrCreateAsync(owner, ctx.CancellationToken);
        var factory = ctx.Get<AlpacaApiFactory>();
        var modes = args.Mode switch
        {
            AccountScope.Paper => new[] { true },
            AccountScope.Live => [false],
            _ => [true, false],
        };

        var results = new List<ProbeResult>();
        foreach (var isPaper in modes)
        {
            var mode = isPaper ? "paper" : "live";
            var (keyId, secret) = isPaper ? (keys.AlpacaApiKeyId, keys.AlpacaApiSecretKey) : (keys.AlpacaLiveApiKeyId, keys.AlpacaLiveApiSecretKey);
            if (keyId is null || secret is null)
            {
                results.Add(new(mode, false, KeyNames.Prefix(null), null, null, null, null, "no keys in Vault"));
                continue;
            }
            var started = ctx.Clock.GetTimestamp();
            try
            {
                await using var api = factory.ForKeys(keyId, secret, isPaper);
                var acct = await api.GetAsync("/v2/account", ct: ctx.CancellationToken);
                var ms = (long)ctx.Clock.GetElapsedTime(started).TotalMilliseconds;
                results.Add(new(mode, true, KeyNames.Prefix(keyId),
                    Str(acct, "account_number") is { } n ? SecretRedactor.MaskAccountNumber(n) : null,
                    Str(acct, "status"),
                    acct.TryGetProperty("trading_blocked", out var tb) && tb.ValueKind == JsonValueKind.True,
                    ms, null));
            }
            catch (AlpacaApiException ex)
            {
                results.Add(new(mode, false, KeyNames.Prefix(keyId), null, null, null, null, $"{(int)ex.Status} {ex.AlpacaMessage}"));
            }
            catch (HttpRequestException ex)
            {
                results.Add(new(mode, false, KeyNames.Prefix(keyId), null, null, null, null, ex.Message));
            }
        }

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("account").Column("ok").Column("key id").Column("number").Column("status").Column("blocked").Column("ms", Align.Right).Column("error", maxWidth: 48);
        foreach (var r in results)
            table.Row(r.Account, r.Ok ? "yes" : "NO", r.KeyId, r.Number ?? "-", r.Status ?? "-",
                r.TradingBlocked is null ? "-" : r.TradingBlocked.Value ? "YES" : "no", r.Millis?.ToString() ?? "-", r.Error ?? "");
        var text = table.Render();
        if (!string.Equals(keys.DefaultBroker, "alpaca", StringComparison.OrdinalIgnoreCase))
            text += "\n\nRouting is Sandbox: these keys work for reads, but orders go to the Sandbox until `idiotproof keys opt-in alpaca`.";

        return results.Any(r => r.Ok) || results.All(r => r.Error == "no keys in Vault")
            ? OperationResult.Ok(results, text)
            : OperationResult.Fail("No stored Alpaca pair answered: " + string.Join("; ", results.Select(r => $"{r.Account}: {r.Error}")), results);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>One account's probe (JSON: account, ok, key_id, number, status, trading_blocked, millis, error).</summary>
    internal sealed record ProbeResult(string Account, bool Ok, string KeyId, string? Number, string? Status, bool? TradingBlocked, long? Millis, string? Error);
}
