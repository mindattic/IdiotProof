using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Blazor.Services;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Account;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Crypto;

public sealed record CryptoWalletsArgs
{
    [Option("asset", Description = "Only this coin's deposit wallet, e.g. BTC (default: every wallet Alpaca returns).")]
    public string? Asset { get; init; }

    [Option("network", Description = "Chain for multi-chain coins, e.g. ethereum or solana.")]
    public string? Network { get; init; }

    [Option("mode", Description = "paper, live or all (default all). Crypto funding is usually live-only.")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

public sealed record CryptoTransfersArgs
{
    [Option("id", Description = "Show one transfer by its id.")]
    public string? Id { get; init; }

    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

public sealed record CryptoWhitelistArgs
{
    [Option("mode", Description = "paper, live or all (default all).")]
    public AccountScope Mode { get; init; } = AccountScope.All;
}

public sealed record CryptoFeeEstimateArgs
{
    [Argument(0, Name = "asset", Description = "Coin to send, e.g. BTC.")]
    public string Asset { get; init; } = "";

    [Argument(1, Name = "amount", Description = "Amount of the coin to send.")]
    public decimal Amount { get; init; }

    [Argument(2, Name = "to", Description = "Destination address.")]
    public string To { get; init; } = "";

    [Option("from", Description = "Source address (default: your Alpaca wallet).")]
    public string? From { get; init; }

    [Option("mode", Description = "paper or live (default paper).")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

public sealed record CryptoWithdrawArgs
{
    [Argument(0, Name = "asset", Description = "Coin to withdraw, e.g. BTC.")]
    public string Asset { get; init; } = "";

    [Argument(1, Name = "amount", Description = "Amount of the coin to send.")]
    public decimal Amount { get; init; }

    [Argument(2, Name = "address", Description = "Destination address (must be on your whitelist).")]
    public string Address { get; init; } = "";

    [Option("mode", Description = "paper or live (default paper). Always needs a confirmation code.")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;

    [Option("note", Description = "Why: kept in the audit trail.")]
    public string? Note { get; init; }
}

public sealed record CryptoWhitelistAddArgs
{
    [Argument(0, Name = "asset", Description = "Coin the address receives, e.g. BTC.")]
    public string Asset { get; init; } = "";

    [Argument(1, Name = "address", Description = "Address to allow withdrawals to.")]
    public string Address { get; init; } = "";

    [Option("mode", Description = "paper or live (default paper). Always needs a confirmation code.")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

public sealed record CryptoWhitelistRemoveArgs
{
    [Argument(0, Name = "id", Description = "Whitelist entry id (from `idiotproof crypto whitelist`).")]
    public string Id { get; init; } = "";

    [Option("mode", Description = "paper or live (default paper). Always needs a confirmation code.")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

/// <summary>
/// Crypto funding plumbing: per-account reads that fail closed with Alpaca's own message (most
/// accounts are not enabled for crypto transfers), and the confirmation gate every funding
/// mutation passes through. Moving coins off the platform is irreversible, so withdrawals and
/// whitelist changes ALWAYS need a code, on paper as well as live.
/// </summary>
internal static class CryptoFunding
{
    public const string ConfirmationKind = "crypto-funding";

    /// <summary>One account's read: its JSON, or why it could not be read.</summary>
    public sealed record AccountRead(BrokerMode Mode, JsonElement? Json, string? Error)
    {
        public string Name => Mode == BrokerMode.Live ? "Live" : "Paper";
    }

    public static IEnumerable<BrokerMode> Modes(AccountScope scope) => scope switch
    {
        AccountScope.Paper => [BrokerMode.Paper],
        AccountScope.Live => [BrokerMode.Live],
        _ => [BrokerMode.Paper, BrokerMode.Live],
    };

    public static async Task<List<AccountRead>> ReadAsync(OperationContext ctx, AccountScope scope, string path, Dictionary<string, string?>? query = null)
    {
        var owner = ctx.RequireOwner();
        var factory = ctx.Get<AlpacaApiFactory>();
        var reads = new List<AccountRead>();
        foreach (var mode in Modes(scope))
        {
            try
            {
                await using var api = await factory.ForOwnerAsync(owner, mode, ctx.CancellationToken);
                reads.Add(new AccountRead(mode, await api.GetAsync(path, query, ctx.CancellationToken), null));
            }
            catch (AlpacaAccessException ex) { reads.Add(new AccountRead(mode, null, ex.Message)); }
            catch (AlpacaApiException ex) { reads.Add(new AccountRead(mode, null, Unavailable(ex))); }
        }
        return reads;
    }

    /// <summary>Alpaca's message, with the hint that funding is an eligibility feature when it is a 403/404/422.</summary>
    public static string Unavailable(AlpacaApiException ex) =>
        (int)ex.Status is 403 or 404 or 422
            ? $"not available on this account ({(int)ex.Status}: {ex.AlpacaMessage})"
            : $"Alpaca {(int)ex.Status}: {ex.AlpacaMessage}";

    public static IEnumerable<JsonElement> Items(JsonElement? json) => json switch
    {
        { ValueKind: JsonValueKind.Array } a => a.EnumerateArray(),
        { ValueKind: JsonValueKind.Object } o => [o],
        _ => [],
    };

    public static string Asset(string asset, string example)
    {
        var a = asset.Trim().ToUpperInvariant();
        if (a.Length == 0 || !a.All(char.IsLetterOrDigit)) throw OperationException.Usage($"Name the coin, e.g. `{example}`.");
        return a;
    }

    public static string Shorten(string? s, int keep = 10) =>
        s is null ? "-" : s.Length <= keep * 2 + 1 ? s : s[..keep] + "…" + s[^keep..];

    public static string When(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? Fmt.Et(CryptoData.Time(e, property)) : "-";

    /// <summary>
    /// The funding gate: the first call previews and issues a code bound to the exact request; the
    /// same call with that code proceeds once. Null = proceed; else the result to return.
    /// </summary>
    public static async Task<OperationResult?> GateAsync(OperationContext ctx, Guid owner, string payload, string preview, object data)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var pending = await store.IssueAsync(owner, ConfirmationKind, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, data) with { Human = preview };
        }
        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationKind && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }

    /// <summary>
    /// Runs a confirmed funding call: audits it, and turns Alpaca's refusal into a Fail carrying
    /// Alpaca's message (nothing retried, nothing guessed).
    /// </summary>
    public static async Task<OperationResult> ExecuteAsync(OperationContext ctx, Guid owner, BrokerMode mode, HttpMethod method, string path,
        object? body, string preview, string payload, Func<JsonElement, (object Data, string Done)> render)
    {
        var audit = ctx.Get<AuditLogRepository>();
        try
        {
            await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, mode, ctx.CancellationToken);
            var json = await api.SendAsync(method, path, body, null, ctx.CancellationToken);
            var (data, done) = render(json);
            await audit.LogAsync("funding", $"Done: {preview.Split('\n')[0]} — {done}", owner, Audit(ctx, "Done", payload, done), ctx.CancellationToken);
            return OperationResult.Ok(data, preview + "\n\n" + done);
        }
        catch (AlpacaApiException ex)
        {
            var why = Unavailable(ex);
            await audit.LogAsync("funding", $"Rejected: {preview.Split('\n')[0]} — {why}", owner, Audit(ctx, "Rejected", payload, why), ctx.CancellationToken);
            return OperationResult.Fail($"Alpaca refused: {why}. Nothing was moved.", new { status = (int)ex.Status, code = ex.AlpacaCode, message = ex.AlpacaMessage });
        }
    }

    private static string Audit(OperationContext ctx, string status, string payload, string message) => JsonSerializer.Serialize(new
    {
        status,
        caller = ctx.Caller.ToString().ToLowerInvariant(),
        request = JsonDocument.Parse(payload).RootElement,
        message,
    });
}

[Operation("crypto wallets", Summary = "Your Alpaca crypto deposit wallets (address per coin) on paper and live; fails closed when funding is not enabled.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto wallets")]
[Example("idiotproof crypto wallets --asset USDC --network ethereum --mode live")]
public sealed class CryptoWalletsOperation : IOperation<CryptoWalletsArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoWalletsArgs args, OperationContext ctx)
    {
        var query = new Dictionary<string, string?>
        {
            ["asset"] = string.IsNullOrWhiteSpace(args.Asset) ? null : CryptoFunding.Asset(args.Asset, "idiotproof crypto wallets --asset BTC"),
            ["network"] = string.IsNullOrWhiteSpace(args.Network) ? null : args.Network.Trim().ToLowerInvariant(),
        };
        var reads = await CryptoFunding.ReadAsync(ctx, args.Mode, "/v2/wallets", query);
        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                blocks.Add($"{r.Name} account: {r.Error}");
                data.Add(new { account = r.Name.ToLowerInvariant(), error = r.Error });
                continue;
            }
            var wallets = CryptoFunding.Items(r.Json).Select(w => new
            {
                asset = CryptoData.Str(w, "asset") ?? CryptoData.Str(w, "symbol") ?? query["asset"],
                network = CryptoData.Str(w, "chain") ?? CryptoData.Str(w, "network") ?? query["network"],
                address = CryptoData.Str(w, "address"),
                assetId = CryptoData.Str(w, "asset_id"),
                created = CryptoFunding.When(w, "created_at"),
            }).ToList();
            data.Add(new { account = r.Name.ToLowerInvariant(), wallets });
            if (wallets.Count == 0) { blocks.Add($"{r.Name} account: no crypto wallets."); continue; }
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Asset").Column("Network").Column("Deposit address").Column("Created");
            foreach (var w in wallets) table.Row(w.asset ?? "-", w.network ?? "-", w.address ?? "-", w.created);
            blocks.Add($"{r.Name} account ({wallets.Count} wallets)\n{table.Render()}");
        }
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}

[Operation("crypto transfers", Summary = "Crypto deposits and withdrawals on your Alpaca accounts: status, amount, fees, addresses (ET).", Risk = OperationRisk.Read)]
[Example("idiotproof crypto transfers")]
[Example("idiotproof crypto transfers --mode live --id 6b1f0c0e-4b8a-4a4e-9b55-0d7c1f2a3b4c")]
public sealed class CryptoTransfersOperation : IOperation<CryptoTransfersArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoTransfersArgs args, OperationContext ctx)
    {
        var path = string.IsNullOrWhiteSpace(args.Id) ? "/v2/wallets/transfers" : $"/v2/wallets/transfers/{Uri.EscapeDataString(args.Id.Trim())}";
        var reads = await CryptoFunding.ReadAsync(ctx, args.Mode, path);
        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                blocks.Add($"{r.Name} account: {r.Error}");
                data.Add(new { account = r.Name.ToLowerInvariant(), error = r.Error });
                continue;
            }
            var transfers = CryptoFunding.Items(r.Json).Select(t => new
            {
                id = CryptoData.Str(t, "id"),
                direction = CryptoData.Str(t, "direction"),
                status = CryptoData.Str(t, "status"),
                asset = CryptoData.Str(t, "asset"),
                amount = CryptoData.Num(t, "amount"),
                usdValue = CryptoData.Num(t, "usd_value"),
                networkFee = CryptoData.Num(t, "network_fee"),
                fees = CryptoData.Num(t, "fees"),
                chain = CryptoData.Str(t, "chain"),
                fromAddress = CryptoData.Str(t, "from_address"),
                toAddress = CryptoData.Str(t, "to_address"),
                txHash = CryptoData.Str(t, "tx_hash"),
                created = CryptoFunding.When(t, "created_at"),
            }).ToList();
            data.Add(new { account = r.Name.ToLowerInvariant(), transfers });
            if (transfers.Count == 0) { blocks.Add($"{r.Name} account: no crypto transfers."); continue; }
            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Created").Column("Dir").Column("Status").Column("Asset").Column("Amount", Align.Right)
                .Column("USD", Align.Right).Column("Fee", Align.Right).Column("To").Column("Id");
            foreach (var t in transfers)
                table.Row(t.created, t.direction ?? "-", t.status ?? "-", t.asset ?? "-", CryptoData.Size(t.amount),
                    t.usdValue is { } u ? Fmt.Money(u) : "-", CryptoData.Size((t.networkFee ?? 0) + (t.fees ?? 0)),
                    CryptoFunding.Shorten(t.toAddress, 6), CryptoFunding.Shorten(t.id, 4));
            blocks.Add($"{r.Name} account ({transfers.Count} transfers)\n{table.Render()}");
        }
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}

[Operation("crypto whitelist", Summary = "Addresses your Alpaca accounts may withdraw crypto to, with their approval status.", Risk = OperationRisk.Read)]
[Example("idiotproof crypto whitelist")]
public sealed class CryptoWhitelistOperation : IOperation<CryptoWhitelistArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoWhitelistArgs args, OperationContext ctx)
    {
        var reads = await CryptoFunding.ReadAsync(ctx, args.Mode, "/v2/wallets/whitelists");
        var blocks = new List<string>();
        var data = new List<object>();
        foreach (var r in reads)
        {
            if (r.Error is not null)
            {
                blocks.Add($"{r.Name} account: {r.Error}");
                data.Add(new { account = r.Name.ToLowerInvariant(), error = r.Error });
                continue;
            }
            var entries = CryptoFunding.Items(r.Json).Select(w => new
            {
                id = CryptoData.Str(w, "id"),
                asset = CryptoData.Str(w, "asset"),
                chain = CryptoData.Str(w, "chain"),
                address = CryptoData.Str(w, "address"),
                status = CryptoData.Str(w, "status"),
                created = CryptoFunding.When(w, "created_at"),
            }).ToList();
            data.Add(new { account = r.Name.ToLowerInvariant(), whitelist = entries });
            if (entries.Count == 0) { blocks.Add($"{r.Name} account: no whitelisted addresses."); continue; }
            var table = new TextTable { MaxWidth = ctx.Width }
                .Column("Asset").Column("Chain").Column("Address").Column("Status").Column("Created").Column("Id");
            foreach (var w in entries) table.Row(w.asset ?? "-", w.chain ?? "-", w.address ?? "-", w.status ?? "-", w.created, w.id ?? "-");
            blocks.Add($"{r.Name} account ({entries.Count} addresses)\n{table.Render()}");
        }
        return OperationResult.Ok(data, string.Join("\n\n", blocks));
    }
}

[Operation("crypto fee-estimate", Summary = "Alpaca's network fee estimate for sending a coin to an address (read-only, nothing is sent).", Risk = OperationRisk.Read)]
[Example("idiotproof crypto fee-estimate BTC 0.01 bc1qexampleaddress --mode live")]
public sealed class CryptoFeeEstimateOperation : IOperation<CryptoFeeEstimateArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoFeeEstimateArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var asset = CryptoFunding.Asset(args.Asset, "idiotproof crypto fee-estimate BTC 0.01 <address>");
        if (args.Amount <= 0) return OperationResult.Usage("The amount must be positive.");
        if (string.IsNullOrWhiteSpace(args.To)) return OperationResult.Usage("Name the destination address.");

        await using var api = await ctx.Get<AlpacaApiFactory>().ForOwnerAsync(owner, args.Mode, ctx.CancellationToken);
        JsonElement json;
        try
        {
            json = await api.GetAsync("/v2/wallets/fees/estimate", new Dictionary<string, string?>
            {
                ["asset"] = asset,
                ["from_address"] = args.From?.Trim(),
                ["to_address"] = args.To.Trim(),
                ["amount"] = args.Amount.ToString(CultureInfo.InvariantCulture),
            }, ctx.CancellationToken);
        }
        catch (AlpacaApiException ex)
        {
            return OperationResult.Fail($"{args.Mode} account: {CryptoFunding.Unavailable(ex)}", new { status = (int)ex.Status, message = ex.AlpacaMessage });
        }
        var fee = CryptoData.Num(json, "fee");
        var text = $"Sending {CryptoData.Size(args.Amount)} {asset} to {CryptoFunding.Shorten(args.To.Trim())} on {args.Mode}: estimated network fee {CryptoData.Size(fee)} {asset}"
            + (fee is { } f && args.Amount > 0 ? $" ({Fmt.Percent(f / args.Amount, 3)} of the amount)." : ".");
        return OperationResult.Ok(new { asset, amount = args.Amount, to = args.To.Trim(), mode = args.Mode.ToString().ToLowerInvariant(), fee }, text);
    }
}

[Operation("crypto withdraw", Summary = "Send coins from your Alpaca account to a whitelisted address. Always previews and needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof crypto withdraw BTC 0.01 bc1qexampleaddress --mode live", Description = "Prints the preview and a confirmation code; nothing moves yet.")]
public sealed class CryptoWithdrawOperation : IOperation<CryptoWithdrawArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoWithdrawArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var asset = CryptoFunding.Asset(args.Asset, "idiotproof crypto withdraw BTC 0.01 <address>");
        if (args.Amount <= 0) return OperationResult.Usage("The amount must be positive.");
        var address = args.Address.Trim();
        if (address.Length == 0) return OperationResult.Usage("Name the destination address.");
        if (args.Mode == BrokerMode.Sandbox) return OperationResult.Usage("Sandbox is IdiotProof's simulator and holds no coins; use --mode paper or --mode live.");

        var amount = args.Amount.ToString(CultureInfo.InvariantCulture);
        var body = new Dictionary<string, object?> { ["amount"] = amount, ["address"] = address, ["asset"] = asset };
        var payload = JsonSerializer.Serialize(new { action = "withdraw", mode = args.Mode.ToString(), body });
        var preview = $"WITHDRAW {CryptoData.Size(args.Amount)} {asset} · {args.Mode}\n  to        {address}\n  effect    coins leave your Alpaca account; a sent transfer cannot be recalled";
        var data = new { action = "withdraw", asset, amount = args.Amount, address, mode = args.Mode.ToString().ToLowerInvariant() };

        var gate = await CryptoFunding.GateAsync(ctx, owner, payload, preview, data);
        if (gate is not null) return gate;

        return await CryptoFunding.ExecuteAsync(ctx, owner, args.Mode, HttpMethod.Post, "/v2/wallets/transfers", body, preview, payload, json =>
        {
            var id = CryptoData.Str(json, "id");
            var status = CryptoData.Str(json, "status");
            return (new { data.action, asset, amount = args.Amount, address, data.mode, transferId = id, status },
                $"Withdrawal submitted to Alpaca {args.Mode}: transfer {id ?? "?"} is {status ?? "pending"}. Track it with `idiotproof crypto transfers --mode {data.mode}`.");
        });
    }
}

[Operation("crypto whitelist-add", Summary = "Ask Alpaca to whitelist a withdrawal address. Always previews and needs a confirmation code.", Risk = OperationRisk.Money)]
[Example("idiotproof crypto whitelist-add BTC bc1qexampleaddress --mode live")]
public sealed class CryptoWhitelistAddOperation : IOperation<CryptoWhitelistAddArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoWhitelistAddArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var asset = CryptoFunding.Asset(args.Asset, "idiotproof crypto whitelist-add BTC <address>");
        var address = args.Address.Trim();
        if (address.Length == 0) return OperationResult.Usage("Name the address to whitelist.");
        if (args.Mode == BrokerMode.Sandbox) return OperationResult.Usage("Sandbox is IdiotProof's simulator; use --mode paper or --mode live.");

        var body = new Dictionary<string, object?> { ["address"] = address, ["asset"] = asset };
        var payload = JsonSerializer.Serialize(new { action = "whitelist-add", mode = args.Mode.ToString(), body });
        var preview = $"WHITELIST {asset} address · {args.Mode}\n  address   {address}\n  effect    withdrawals to this address become possible once Alpaca approves it";
        var data = new { action = "whitelist-add", asset, address, mode = args.Mode.ToString().ToLowerInvariant() };

        var gate = await CryptoFunding.GateAsync(ctx, owner, payload, preview, data);
        if (gate is not null) return gate;

        return await CryptoFunding.ExecuteAsync(ctx, owner, args.Mode, HttpMethod.Post, "/v2/wallets/whitelists", body, preview, payload, json =>
        {
            var id = CryptoData.Str(json, "id");
            var status = CryptoData.Str(json, "status");
            return (new { data.action, asset, address, data.mode, id, status }, $"Whitelist entry {id ?? "?"} created on {args.Mode}: {status ?? "pending approval"}.");
        });
    }
}

[Operation("crypto whitelist-remove", Summary = "Remove a whitelisted withdrawal address. Always previews and needs a confirmation code.", Risk = OperationRisk.Write)]
[Example("idiotproof crypto whitelist-remove 2a9c7f3e-11aa-4bcd-9e01-6f0b2c3d4e5f --mode live")]
public sealed class CryptoWhitelistRemoveOperation : IOperation<CryptoWhitelistRemoveArgs>
{
    public async Task<OperationResult> ExecuteAsync(CryptoWhitelistRemoveArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        var id = args.Id.Trim();
        if (id.Length == 0) return OperationResult.Usage("Name the whitelist entry id (see `idiotproof crypto whitelist`).");
        if (args.Mode == BrokerMode.Sandbox) return OperationResult.Usage("Sandbox is IdiotProof's simulator; use --mode paper or --mode live.");

        var payload = JsonSerializer.Serialize(new { action = "whitelist-remove", mode = args.Mode.ToString(), id });
        var preview = $"REMOVE whitelist entry {id} · {args.Mode}\n  effect    withdrawals to that address stop being allowed";
        var data = new { action = "whitelist-remove", id, mode = args.Mode.ToString().ToLowerInvariant() };

        var gate = await CryptoFunding.GateAsync(ctx, owner, payload, preview, data);
        if (gate is not null) return gate;

        return await CryptoFunding.ExecuteAsync(ctx, owner, args.Mode, HttpMethod.Delete, $"/v2/wallets/whitelists/{Uri.EscapeDataString(id)}", null, preview, payload,
            _ => (data, $"Whitelist entry {id} removed on {args.Mode}."));
    }
}
