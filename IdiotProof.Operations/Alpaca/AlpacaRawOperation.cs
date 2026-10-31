using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Core.Trading;

namespace IdiotProof.Operations.Alpaca;

public sealed record AlpacaRawArgs
{
    [Argument(0, Name = "method", Description = "GET, POST, PATCH, PUT or DELETE.")]
    public string Method { get; init; } = "";

    [Argument(1, Name = "path", Description = "The Alpaca path with any query string, e.g. /v2/account or /v2/assets?status=active.")]
    public string Path { get; init; } = "";

    [Option("body", Description = "JSON request body for POST/PATCH/PUT (use '-' to read it from stdin).")]
    public string? Body { get; init; }

    [Option("data", Description = "Call the Market Data API (data.alpaca.markets, GET only) instead of the Trading API.")]
    public bool Data { get; init; }

    [Option("mode", Description = "Trading API account: paper (default) or live. Live mutations need a confirmation code.")]
    public BrokerMode Mode { get; init; } = BrokerMode.Paper;
}

/// <summary>
/// The escape hatch that keeps every Alpaca endpoint reachable before a typed operation exists
/// (IP-T327): any verb on any Trading API path, or a GET on the Market Data API, through the
/// owner's <see cref="AlpacaApiFactory"/> client. It is not a way around the money path: a
/// mutation under <c>/v2/orders</c>, <c>/v2/positions</c> (closes, exercise, do-not-exercise) or
/// <c>/v2/wallets</c> (crypto transfers and whitelists) is refused with the gated operation to use
/// instead, and any other Live mutation needs a confirmation code bound to the exact request.
/// </summary>
[Operation("alpaca raw", Summary = "Any Alpaca endpoint (escape hatch): GET/POST/PATCH/PUT/DELETE a Trading API path, or GET a Market Data path with --data; order, position and wallet mutations are refused (use the gated operations).", Risk = OperationRisk.Write)]
[Example("idiotproof alpaca raw GET /v2/account")]
[Example("idiotproof alpaca raw GET /v2/assets/AAPL --mode live")]
[Example("idiotproof alpaca raw GET /v2/stocks/AAPL/snapshot --data")]
[Example("idiotproof alpaca raw POST /v2/watchlists --body \"{\\\"name\\\":\\\"gappers\\\",\\\"symbols\\\":[\\\"AAPL\\\"]}\"")]
public sealed class AlpacaRawOperation : IOperation<AlpacaRawArgs>
{
    /// <summary>The <see cref="ConfirmationStore"/> kind a Live raw mutation's code is issued under.</summary>
    public const string ConfirmationKind = "alpaca-raw";

    private static readonly HttpMethod[] Methods = [HttpMethod.Get, HttpMethod.Post, HttpMethod.Patch, HttpMethod.Put, HttpMethod.Delete];

    /// <summary>Path prefixes whose mutations move money or positions, and the gated operations that own them.</summary>
    internal static readonly (string Prefix, string Use)[] Gated =
    [
        ("/v2/orders", "`orders place|bracket|oco|oto|replace|cancel|cancel-all`, `trade buy|sell|short|cover`, `options buy|sell|spread`, `crypto buy|sell`"),
        ("/v2/positions", "`positions close|close-all`, `trade flatten`, `options close|exercise|do-not-exercise`"),
        ("/v2/wallets", "`crypto withdraw`, `crypto whitelist-add|whitelist-remove`"),
    ];

    public async Task<OperationResult> ExecuteAsync(AlpacaRawArgs args, OperationContext ctx)
    {
        var method = Methods.FirstOrDefault(m => string.Equals(m.Method, args.Method?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (method is null) return OperationResult.Usage("method must be GET, POST, PATCH, PUT or DELETE.");

        var raw = (args.Path ?? "").Trim();
        if (!raw.StartsWith('/') || raw.StartsWith("//", StringComparison.Ordinal) || raw.Contains("://", StringComparison.Ordinal))
            return OperationResult.Usage("path must be an Alpaca path starting with '/', e.g. /v2/account (no host).");
        var normalized = Normalize(raw);
        if (normalized is null) return OperationResult.Usage("path may not contain '..' segments or backslashes.");

        JsonElement? body = null;
        if (!string.IsNullOrWhiteSpace(args.Body))
        {
            if (method == HttpMethod.Get || method == HttpMethod.Delete)
                return OperationResult.Usage($"{method.Method} takes no --body; put parameters in the path's query string.");
            try { body = JsonDocument.Parse(args.Body).RootElement.Clone(); }
            catch (JsonException ex) { return OperationResult.Usage($"--body is not valid JSON: {ex.Message}"); }
        }

        var mutates = method != HttpMethod.Get;
        if (args.Data && mutates)
            return OperationResult.Usage("The Market Data API is read-only: --data takes GET.");

        if (mutates && Refusal(normalized) is { } refusal)
            return OperationResult.Blocked(refusal, new { method = method.Method, path = raw, refused = true });

        var owner = ctx.RequireOwner();
        var host = args.Data ? "data" : args.Mode == BrokerMode.Live ? "live" : "paper";
        if (mutates && args.Mode == BrokerMode.Live)
        {
            var gate = await RequireCodeAsync(ctx, owner, method.Method, raw, body);
            if (gate is not null) return gate;
        }

        var factory = ctx.Get<AlpacaApiFactory>();
        await using var api = args.Data
            ? await factory.ForDataAsync(owner, ctx.CancellationToken)
            : await factory.ForOwnerAsync(owner, args.Mode, ctx.CancellationToken);

        JsonElement response;
        try
        {
            response = args.Data
                ? await api.GetDataAsync(raw, ct: ctx.CancellationToken)
                : await api.SendAsync(method, raw, body, ct: ctx.CancellationToken);
        }
        catch (AlpacaApiException ex)
        {
            return OperationResult.Fail($"{method.Method} {raw} ({host}) -> {(int)ex.Status} {ex.AlpacaMessage}",
                new { method = method.Method, path = raw, host, status = (int)ex.Status, code = ex.AlpacaCode, message = ex.AlpacaMessage });
        }

        var empty = response.ValueKind == JsonValueKind.Undefined;
        var text = empty ? "(no content)" : JsonSerializer.Serialize(response, OperationJson.Indented);
        return OperationResult.Ok(
            new { method = method.Method, path = raw, host, response = empty ? (JsonElement?)null : response },
            $"{method.Method} {raw} ({host}){Environment.NewLine}{text}");
    }

    /// <summary>
    /// The lower-case, percent-decoded path without its query, with repeated slashes collapsed and
    /// <c>/./</c> removed, so <c>/V2//Orders</c> and <c>/v2/%6Frders</c> are seen as <c>/v2/orders</c>.
    /// Null for a path that tries to climb (<c>..</c>) or uses backslashes.
    /// </summary>
    internal static string? Normalize(string path)
    {
        var p = path.Split('?', 2)[0];
        for (var i = 0; i < 3; i++)
        {
            var decoded = Uri.UnescapeDataString(p);
            if (decoded == p) break;
            p = decoded;
        }
        if (p.Contains('\\') || p.Split('/').Any(s => s == "..")) return null;
        p = p.ToLowerInvariant();
        while (p.Contains("//", StringComparison.Ordinal)) p = p.Replace("//", "/", StringComparison.Ordinal);
        while (p.Contains("/./", StringComparison.Ordinal)) p = p.Replace("/./", "/", StringComparison.Ordinal);
        if (p.EndsWith("/.", StringComparison.Ordinal)) p = p[..^2];
        return p.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
    }

    /// <summary>Why a mutation of <paramref name="normalized"/> is refused, or null when it is not on the money path.</summary>
    internal static string? Refusal(string normalized)
    {
        foreach (var (prefix, use) in Gated)
        {
            if (normalized == prefix || normalized.StartsWith(prefix + "/", StringComparison.Ordinal) || normalized.StartsWith(prefix + ":", StringComparison.Ordinal))
                return $"`alpaca raw` does not mutate {prefix}: orders, position closes, exercises and wallet transfers go through the TransactionGateway and confirmation codes. Use {use}.";
        }
        if (normalized.Split('/').Any(s => s is "exercise" or "do-not-exercise"))
            return "`alpaca raw` does not exercise options. Use `options exercise` or `options do-not-exercise`.";
        return null;
    }

    /// <summary>Null when the caller may proceed (a valid code for exactly this request), else the result to return.</summary>
    private static async Task<OperationResult?> RequireCodeAsync(OperationContext ctx, Guid owner, string method, string path, JsonElement? body)
    {
        var payload = JsonSerializer.Serialize(new { account = "live", method, path, body = body?.GetRawText() });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var preview = $"LIVE {method} {path}" + (body is { } b ? $" with body {Shorten(b.GetRawText())}" : "") +
                      " on your real-money Alpaca account.";
        var store = ctx.Get<ConfirmationStore>();
        if (string.IsNullOrWhiteSpace(ctx.ConfirmationCode))
        {
            var pending = await store.IssueAsync(owner, ConfirmationKind, payload, hash, preview, ctx.CancellationToken);
            return OperationResult.NeedsConfirmation(pending.Code, preview, pending.ExpiresUtc, new { account = "live", method, path });
        }
        var redemption = await store.RedeemAsync(ctx.ConfirmationCode, owner, ctx.Caller.ToString().ToLowerInvariant(),
            p => p.Kind == ConfirmationKind && p.PayloadJson == payload ? hash : "mismatch", ctx.CancellationToken);
        return redemption.Succeeded ? null : OperationResult.Blocked(redemption.Message, new { failure = redemption.Failure.ToString() });
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s[..199] + "…";
}
