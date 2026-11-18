using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using IdiotProof.Core.Data;
namespace IdiotProof.Core.Services;

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
/// Several markers script a conversation with the repair loop (IP-T524): the first
/// call gets the first marker's script, repair round N gets marker N+1 (the round
/// number is read from the request's "Repair round N" line), and when the rounds
/// outnumber the markers the last one repeats. So "[[script: bad]] [[script: good]]"
/// is "bad output, then good output", and one marker is the same bad output every time.
/// The handler keeps no state between calls.
///
/// This class never ships active: Program.cs gates the registration on
/// Development + the env flag, and nothing else references it.
/// </summary>
public sealed class FakeLlmHandler : HttpMessageHandler
{
    private static readonly Regex ScriptMarker =
        new(@"\[\[script:\s*(.+?)\s*\]\]", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RepairRound =
        new(Regex.Escape(GenerationFeedback.RoundMarker) + @"(\d+)", RegexOptions.Compiled);

    private static readonly Regex TickerLine =
        new(@"Ticker:\s*([A-Za-z\.]+)", RegexOptions.Compiled);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var markers = ScriptMarker.Matches(body);
        string script;
        if (markers.Count > 0)
        {
            var round = RepairRound.Match(body) is { Success: true } r && int.TryParse(r.Groups[1].Value, out var n) ? n : 0;
            var marker = markers[Math.Min(round, markers.Count - 1)];
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
