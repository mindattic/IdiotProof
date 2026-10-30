using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Operations;

namespace IdiotProof.Cli;

/// <summary>How a Hub call ended, from the client's side.</summary>
public enum HubCallKind
{
    /// <summary>The Hub ran (or refused) the operation; see <see cref="HubCallResult.Response"/>.</summary>
    Completed,

    /// <summary>Nothing answers at the Hub URL.</summary>
    HubDown,

    /// <summary>No key to send: the Hub has never started on this account and no IDIOTPROOF_HUB_KEY is set.</summary>
    NoKey,

    /// <summary>The Hub answered 401: the key this client sent is not the Hub's.</summary>
    Unauthorized,

    /// <summary>Any other HTTP failure or an unreadable response.</summary>
    HttpError,

    /// <summary>Ctrl+C.</summary>
    Cancelled,

    /// <summary>The 2-hour ceiling passed.</summary>
    TimedOut,
}

/// <summary>The outcome of <see cref="HubClient.RunAsync"/>: the Hub's response, or why there is none.</summary>
public sealed record HubCallResult(HubCallKind Kind, HubOperationResponse? Response, string? Error)
{
    /// <summary>The process exit code: the Hub's own for a completed call, else <see cref="CliExitCodes.Failed"/>.</summary>
    public int ExitCode => Kind == HubCallKind.Completed && Response is not null ? Response.ExitCode : CliExitCodes.Failed;
}

/// <summary>
/// The CLI's one channel to the Hub (IP-T69). Fail-closed: a health gate first, so a missing Hub
/// is a clear "not running" (exit 1) and never a hang; the shared key on every call; a notice on
/// stderr once a call has run for 8 s (output arrives only when it finishes); a 2-hour ceiling;
/// Ctrl+C cancels the wait. Requests are written with <see cref="OperationJson.Options"/>, the
/// Hub's own wire contract.
/// </summary>
public sealed class HubClient : IDisposable
{
    public static readonly TimeSpan DefaultHealthTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultSlowNotice = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromHours(2);

    private readonly HttpClient http;
    private readonly Func<string?> keySource;

    public HubClient(Uri baseUrl, HttpMessageHandler? handler = null, Func<string?>? keySource = null)
    {
        BaseUrl = baseUrl;
        http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.BaseAddress = baseUrl;
        http.Timeout = Timeout.InfiniteTimeSpan; // per-call deadlines below
        this.keySource = keySource ?? HubKey.ForClient;
    }

    public Uri BaseUrl { get; }

    public TimeSpan HealthTimeout { get; init; } = DefaultHealthTimeout;
    public TimeSpan SlowNotice { get; init; } = DefaultSlowNotice;
    public TimeSpan CallTimeout { get; init; } = DefaultCallTimeout;

    /// <summary>The friendly "not running" line every caller prints.</summary>
    public string NotRunningMessage =>
        $"Hub not running at {BaseUrl} — start it with `{OperationDescriptor.ProgramName} hub start`.";

    /// <summary><c>GET /api/health</c>, or null when nothing healthy answers in time.</summary>
    public async Task<HubHealth?> TryGetHealthAsync(CancellationToken ct = default)
    {
        var body = await TryGetStringAsync(HubWire.HealthPath, HealthTimeout, ct);
        if (body is null) return null;
        try { return JsonSerializer.Deserialize<HubHealth>(body, OperationJson.Options); }
        catch (JsonException) { return null; }
    }

    /// <summary><c>GET /api/catalog</c> as raw JSON, or null.</summary>
    public Task<string?> TryGetCatalogJsonAsync(CancellationToken ct = default) =>
        TryGetStringAsync(HubWire.CatalogPath, TimeSpan.FromSeconds(10), ct);

    /// <summary><c>GET /api/version</c>, or null.</summary>
    public async Task<string?> TryGetVersionAsync(CancellationToken ct = default) =>
        (await TryGetStringAsync("/api/version", HealthTimeout, ct))?.Trim();

    /// <summary>
    /// Runs one operation on the Hub. Never throws for transport problems: they come back as a
    /// <see cref="HubCallKind"/> with a message a person can act on.
    /// </summary>
    public async Task<HubCallResult> RunAsync(HubOperationRequest request, TextWriter stderr, CancellationToken ct = default)
    {
        if (await TryGetHealthAsync(ct) is null)
            return ct.IsCancellationRequested
                ? new HubCallResult(HubCallKind.Cancelled, null, "Cancelled.")
                : new HubCallResult(HubCallKind.HubDown, null, NotRunningMessage);

        var key = keySource();
        if (string.IsNullOrWhiteSpace(key))
            return new HubCallResult(HubCallKind.NoKey, null,
                $"No Hub key found. The Hub creates one (Vault Tokens entry '{HubWire.KeyTokenName}') the first time it starts " +
                $"on this Windows account — the Hub answering now was started under another account or with {HubWire.KeyEnvVar}. " +
                $"Set {HubWire.KeyEnvVar} to the Hub's key, or restart the Hub as this user (`{OperationDescriptor.ProgramName} hub start`).");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CallTimeout);

        using var message = new HttpRequestMessage(HttpMethod.Post, HubWire.OperationPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, OperationJson.Options), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add(HubWire.KeyHeader, key);

        HttpResponseMessage response;
        try
        {
            var send = http.SendAsync(message, deadline.Token);
            var name = request.Argv is { Count: > 0 } argv ? string.Join(' ', argv.TakeWhile(t => !t.StartsWith('-')).Take(3)) : request.Operation;
            if (await Task.WhenAny(send, Task.Delay(SlowNotice, deadline.Token)) != send
                && !send.IsCompleted && !deadline.IsCancellationRequested)
                await stderr.WriteLineAsync($"[hub] `{name}` is still running on the Hub… its output arrives when it finishes. " +
                                            "Ctrl+C stops waiting here; it does not undo work the Hub already did.");
            response = await send;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new HubCallResult(HubCallKind.Cancelled, null,
                "Cancelled. The Hub may still finish the operation — check before re-running anything that places orders.");
        }
        catch (OperationCanceledException)
        {
            return new HubCallResult(HubCallKind.TimedOut, null,
                $"No answer from the Hub after {CallTimeout.TotalHours:0.#} h; the operation may still be running there.");
        }
        catch (HttpRequestException ex)
        {
            return new HubCallResult(HubCallKind.HubDown, null, $"{NotRunningMessage} ({ex.Message})");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new HubCallResult(HubCallKind.Unauthorized, null,
                    $"The Hub rejected this CLI's key ({HubWire.KeyHeader}). Both sides read it from {HubWire.KeyEnvVar} when set, " +
                    $"else the Vault Tokens entry '{HubWire.KeyTokenName}' of the Windows account they run as — make them match " +
                    "(same account, or the same IDIOTPROOF_HUB_KEY for both).");
            if (!response.IsSuccessStatusCode)
                return new HubCallResult(HubCallKind.HttpError, null, $"The Hub answered {(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(body)}");

            try
            {
                var parsed = JsonSerializer.Deserialize<HubOperationResponse>(body, OperationJson.Options);
                return parsed is null
                    ? new HubCallResult(HubCallKind.HttpError, null, "The Hub sent an empty response.")
                    : new HubCallResult(HubCallKind.Completed, parsed, null);
            }
            catch (JsonException ex)
            {
                return new HubCallResult(HubCallKind.HttpError, null, $"Unreadable Hub response ({ex.Message}): {Shorten(body)}");
            }
        }
    }

    private async Task<string?> TryGetStringAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await http.GetAsync(path, cts.Token);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cts.Token) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Shorten(string body) => body.Length > 400 ? body[..400] + "…" : body;

    public void Dispose() => http.Dispose();
}
