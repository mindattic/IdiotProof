namespace IdiotProof.Core.Services;

/// <summary>
/// Backs off on HTTP 429 instead of failing the operation (IP-T596): honors Retry-After (capped at 2 minutes), else 5/15/45 s,
/// and gives up after <see cref="MaxRetries"/> retries by returning the last 429 response for the caller to handle.
/// </summary>
public static class HttpRateLimitRetry
{
    public const int MaxRetries = 3;

    public static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var resp = await send();
            if ((int)resp.StatusCode != 429 || attempt >= MaxRetries) return resp;
            var pause = resp.Headers.RetryAfter?.Delta is { } ra && ra > TimeSpan.Zero
                ? (ra < TimeSpan.FromMinutes(2) ? ra : TimeSpan.FromMinutes(2))
                : TimeSpan.FromSeconds(5 * Math.Pow(3, attempt));
            resp.Dispose();
            await Task.Delay(pause, ct);
        }
    }
}
