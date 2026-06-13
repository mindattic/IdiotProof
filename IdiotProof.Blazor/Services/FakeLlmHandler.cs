using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// E2E test seam for the LLM gateway. When the host starts with
/// IDIOTPROOF_FAKE_LLM=1 (Development only), the LegionClient registration is
/// swapped for one whose HttpClient terminates here instead of reaching
/// api.anthropic.com — Cypress runs stay deterministic and never spend tokens.
///
/// The response is Anthropic-Messages-shaped (content[0].text), which is the
/// only field Legion's claude provider reads. The script returned is:
///   1. the contents of a [[script: ...]] marker if the spec embedded one in
///      the Describe-tab prose (lets each spec choose its own IdiotScript), or
///   2. a default reclaim chain on the ticker named in the user message.
///
/// This class never ships active: Program.cs gates the registration on
/// Development + the env flag, and nothing else references it.
/// </summary>
public sealed class FakeLlmHandler : HttpMessageHandler
{
    private static readonly Regex ScriptMarker =
        new(@"\[\[script:\s*(.+?)\s*\]\]", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TickerLine =
        new(@"Ticker:\s*([A-Za-z\.]+)", RegexOptions.Compiled);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var marker = ScriptMarker.Match(body);
        string script;
        if (marker.Success)
        {
            // The marker travels through JSON serialization, so unescape it
            // back to raw text before returning it as the "model output".
            script = JsonSerializer.Deserialize<string>($"\"{marker.Groups[1].Value}\"") ?? "";
        }
        else
        {
            var ticker = TickerLine.Match(body) is { Success: true } t
                ? t.Groups[1].Value.ToUpperInvariant()
                : "SPY";
            script =
                $"Stock.Ticker(\"{ticker}\").RequireAdxAbove(20).RequireEmaStack(9, 31)" +
                ".OnReclaim(9).WithVolumeConfirm(1.2).Long().StopLoss(9.50).TakeProfit(12.00).Build()";
        }

        var payload = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = script } },
            stop_reason = "end_turn",
        });

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }
}
