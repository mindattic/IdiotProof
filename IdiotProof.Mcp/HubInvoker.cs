using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdiotProof.Operations;

namespace IdiotProof.Mcp;

/// <summary>A Hub reply, or the reason there wasn't one (<c>hub_unreachable</c>, <c>hub_timeout</c>, …).</summary>
public sealed record HubInvocationOutcome(HubOperationResponse? Response, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Forwards MCP tool calls to the Hub over loopback HTTP with the shared key. Never throws to the
/// MCP layer: transport problems come back as structured errors the assistant can explain.
/// </summary>
public sealed class HubInvoker(HttpClient http)
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(30);

    public static HttpClient CreateClient(Uri baseUrl, HttpMessageHandler? handler = null) =>
        new(handler ?? new HttpClientHandler()) { BaseAddress = baseUrl, Timeout = CallTimeout };

    public async Task<HubInvocationOutcome> InvokeAsync(HubOperationRequest request, CancellationToken ct)
    {
        var key = HubKey.ForClient();
        if (key is null)
            return new(null, "hub_key_missing", "The Hub has never started on this machine (no shared key yet). Run `idiotproof hub start`.");

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, HubWire.OperationPath)
            {
                Content = JsonContent.Create(request, options: OperationJson.Options),
            };
            message.Headers.Add(HubWire.KeyHeader, key);
            using var response = await http.SendAsync(message, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(null, "hub_key_rejected", "The Hub rejected the shared key (it was rotated or IDIOTPROOF_HUB_KEY differs). Restart the MCP server.");
            if (!response.IsSuccessStatusCode)
                return new(null, "hub_http_error", $"Hub answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");

            var body = await response.Content.ReadFromJsonAsync<HubOperationResponse>(OperationJson.Options, ct);
            return body is null ? new(null, "hub_http_error", "Empty Hub response.") : new(body, null, null);
        }
        catch (HttpRequestException ex)
        {
            return new(null, "hub_unreachable", $"The IdiotProof Hub is not running at {http.BaseAddress} ({ex.Message}). Start it with `idiotproof hub start`.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(null, "hub_timeout", $"The Hub did not answer within {CallTimeout.TotalMinutes:0} minutes.");
        }
    }

    /// <summary>The live catalog from a running Hub, or null when it isn't reachable.</summary>
    public async Task<JsonNode?> TryGetCatalogAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var text = await http.GetStringAsync(HubWire.CatalogPath, timeout.Token);
            return JsonNode.Parse(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
