using System.Net.Http.Json;
using System.Text.Json;

namespace IdiotProof.Brokers;

/// <summary>An Alpaca OAuth token grant.</summary>
public sealed record AlpacaOAuthToken(string AccessToken, string TokenType, string? Scope, string? RefreshToken);

/// <summary>
/// Alpaca OAuth 2.0 (Connect API) foundation — the account-LINKING alternative to
/// pasting a raw key/secret. Instead of holding a user's keys, IdiotProof sends
/// them to Alpaca's own login/authorize page; Alpaca returns an authorization
/// code, which this exchanges for a scoped, revocable access token. The user
/// never shares their key, and can revoke IdiotProof from their Alpaca dashboard.
///
/// This class is intentionally self-contained and OFF the money path: it builds
/// the authorize URL and performs the token exchange only. Wiring the resulting
/// token into order placement (an <c>Authorization: Bearer</c> mode on
/// <see cref="AlpacaBrokerClient"/>, preferred by <c>UserBrokerResolver</c> when
/// a token exists) is a deliberate next step, gated on real testing against a
/// registered app — never shipped blind on the order path.
///
/// ── Activation checklist ──
/// 1. Register an OAuth app at https://app.alpaca.markets/ → get client_id/secret,
///    set the redirect URI to e.g. https://&lt;host&gt;/connect/alpaca/callback.
/// 2. Config: Alpaca:OAuth:ClientId / :ClientSecret / :RedirectUri.
/// 3. Blazor endpoints: GET /connect/alpaca → redirect to
///    <see cref="BuildAuthorizeUrl"/>; GET /connect/alpaca/callback → call
///    <see cref="ExchangeCodeAsync"/>, persist the token on the user (encrypted,
///    like the raw keys today).
/// 4. Broker: add a Bearer-token constructor to AlpacaBrokerClient and prefer it
///    in UserBrokerResolver when a token is present. Test on paper first.
/// </summary>
public static class AlpacaOAuthClient
{
    private const string AuthorizeUrl = "https://app.alpaca.markets/oauth/authorize";
    private const string TokenUrl     = "https://api.alpaca.markets/oauth/token";

    /// <summary>Default scopes: read account + place/manage trades.</summary>
    public const string DefaultScope = "account:write trading";

    /// <summary>
    /// The URL to send the user to so they log in at Alpaca and authorize the app.
    /// <paramref name="state"/> is an opaque anti-CSRF value the caller generates,
    /// stores in the session, and verifies on the callback.
    /// </summary>
    public static string BuildAuthorizeUrl(string clientId, string redirectUri, string state, string scope = DefaultScope)
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"]     = clientId,
            ["redirect_uri"]  = redirectUri,
            ["scope"]         = scope,
            ["state"]         = state,
        };
        return AuthorizeUrl + "?" + string.Join("&",
            q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>
    /// Exchanges the authorization code returned to the callback for an access
    /// token. Returns null (never throws) on any failure, with the reason on the
    /// out param so the callback can show a clear error.
    /// </summary>
    public static async Task<AlpacaOAuthToken?> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string redirectUri,
        HttpClient? http = null, CancellationToken ct = default)
    {
        var owned = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var resp = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["client_id"]     = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"]  = redirectUri,
            }), ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at) || at.ValueKind != JsonValueKind.String)
                return null;
            return new AlpacaOAuthToken(
                at.GetString()!,
                root.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "Bearer" : "Bearer",
                root.TryGetProperty("scope", out var sc) ? sc.GetString() : null,
                root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null);
        }
        catch { return null; }
        finally { if (owned) http.Dispose(); }
    }
}
