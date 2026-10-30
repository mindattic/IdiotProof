using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IdiotProof.Core.Ledger;

/// <summary>
/// Scrubs operation arguments before they are written to the command ledger. Two nets: any value
/// whose property name says it is a secret (key, secret, password, token, …) and any string that
/// looks like a credential on its own (Anthropic/OpenAI <c>sk-</c> keys, Alpaca <c>PK…</c>/<c>AK…</c>
/// key ids, long opaque base64/hex blobs), wherever it appears. Redaction keeps a short prefix so a
/// ledger reader can still tell which key was used without being able to use it.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "***";

    private static readonly string[] SecretNameParts =
        ["key", "secret", "password", "passwd", "token", "pepper", "credential", "authorization", "cookie", "confirm"];

    /// <summary>True for property names that carry secrets (<c>api_key</c>, <c>alpacaSecret</c>, <c>--password</c>, …).</summary>
    public static bool IsSecretName(string name)
    {
        var n = name.TrimStart('-').ToLowerInvariant();
        // "keys" alone is a noun in our CLI ("keys status"), and a "symbol"/"ticker" is never secret.
        return n != "keys" && SecretNameParts.Any(n.Contains);
    }

    /// <summary>True when a bare value looks like a credential regardless of where it sits.</summary>
    public static bool LooksLikeSecret(string value) =>
        value.Length >= 16 && (VendorKey().IsMatch(value) || OpaqueBlob().IsMatch(value));

    /// <summary>Keeps the first four characters so the reader can tell keys apart; never more than that.</summary>
    public static string MaskValue(string value) =>
        value.Length <= 8 ? Mask : value[..4] + "..." + Mask;

    /// <summary>Redacts a JSON document; invalid JSON is treated as one opaque string.</summary>
    public static string? RedactJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return LooksLikeSecret(json) ? MaskValue(json) : json; }
        return Redact(node, parentIsSecret: false)?.ToJsonString();
    }

    /// <summary>Redacts a CLI argv: the value after a secret-named flag, and any secret-looking token.</summary>
    public static IReadOnlyList<string> RedactArgv(IReadOnlyList<string> argv)
    {
        var result = new List<string>(argv.Count);
        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i];
            var eq = token.IndexOf('=');
            if (token.StartsWith("--") && eq > 0 && IsSecretName(token[..eq]))
            {
                result.Add(token[..(eq + 1)] + Mask);
                continue;
            }
            if (i > 0 && argv[i - 1].StartsWith("--") && !argv[i - 1].Contains('=') && IsSecretName(argv[i - 1]) && !token.StartsWith("--"))
            {
                result.Add(Mask);
                continue;
            }
            result.Add(LooksLikeSecret(token) ? MaskValue(token) : token);
        }
        return result;
    }

    private static JsonNode? Redact(JsonNode? node, bool parentIsSecret)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                    obj[name] = Redact(child?.DeepClone(), IsSecretName(name));
                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    arr[i] = Redact(arr[i]?.DeepClone(), parentIsSecret);
                return arr;
            case JsonValue value when value.TryGetValue<string>(out var s):
                if (parentIsSecret) return JsonValue.Create(string.IsNullOrEmpty(s) ? s : Mask);
                return LooksLikeSecret(s) ? JsonValue.Create(MaskValue(s)) : value;
            case JsonValue value:
                return parentIsSecret ? JsonValue.Create(Mask) : value;
            default:
                return node;
        }
    }

    // sk-ant-…, sk-proj-…, sk-…; Alpaca key ids (PK/AK/CK + 14+ upper alnum).
    [GeneratedRegex(@"^(sk-[A-Za-z0-9_\-]{12,}|[PAC]K[A-Z0-9]{14,})$")]
    private static partial Regex VendorKey();

    // Long opaque blobs with no spaces: 32+ base64/base64url/hex characters (Alpaca secrets, tokens).
    [GeneratedRegex(@"^[A-Za-z0-9+/_\-]{32,}={0,2}$")]
    private static partial Regex OpaqueBlob();
}
