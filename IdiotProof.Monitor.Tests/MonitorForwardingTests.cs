using System.Net;
using System.Text;
using System.Text.Json;
using IdiotProof.Monitor;
using IdiotProof.Operations;

using IdiotProof.Core.Monitoring;
namespace IdiotProof.Monitor.Tests;

/// <summary>IP-T138: the Monitor's old subcommands forward to the equivalent operations through the Hub.</summary>
[NonParallelizable]
public class MonitorForwardingTests
{
    private const string User = "6f1c2f9e-0000-4000-8000-000000000001";

    private static IReadOnlyList<string>? Argv(params string[] args) => MonitorForwarding.Translate(args, out _)?.Argv;

    [Test]
    public void Status_SetKeys_ResyncCanon_PremarketFade_Map()
    {
        Assert.That(Argv("status", "--user", User), Is.EqualTo(new[] { "monitor", "status", "--user", User }));
        Assert.That(Argv("set-keys", "--key", "PK1", "--secret", "s3", "--live", "--force"),
            Is.EqualTo(new[] { "keys", "set", "alpaca-live", "--key-id", "PK1", "--secret", "s3", "--force" }));
        Assert.That(Argv("set-keys", "--key", "PK1", "--secret", "s3"), Does.Contain("alpaca-paper"));
        Assert.That(Argv("resync-canon", "--apply"), Is.EqualTo(new[] { "strategy", "resync-canon", "--apply" }));
        Assert.That(Argv("premarket-fade"), Is.EqualTo(new[] { "gapper", "premarket-fade", "run" }));
        Assert.That(Argv("create-account", "--email", "a@b.com", "--password", "pw"), Is.EqualTo(new[] { "users", "create", "a@b.com", "--password", "pw" }));
        Assert.That(Argv("create-strategies", "--file", "w.json")![2], Is.EqualTo(Path.GetFullPath("w.json")), "the Hub reads an absolute path");
    }

    [Test]
    public void Flatten_OnlyWithASymbol_NeverTheWholeAccount()
    {
        Assert.That(Argv("flatten", "--symbol", "hiho"), Is.EqualTo(new[] { "trade", "flatten", "--strategy", "HIHO" }));

        Assert.That(MonitorForwarding.Translate(["flatten"], out var usage), Is.Null);
        Assert.That(usage, Does.Contain("--strategy").And.Contain("--local"));
    }

    [Test]
    public void LocalFlag_AndUnmappedCommands_StayInProcess()
    {
        Assert.That(MonitorForwarding.Translate(["status", "--local"], out var u1), Is.Null);
        Assert.That(u1, Is.Null);
        Assert.That(MonitorForwarding.Translate(["replay", "--strategy", "x"], out var u2), Is.Null);
        Assert.That(u2, Is.Null);
        Assert.That(MonitorForwarding.Translate(["test-order"], out _), Is.Null);
    }

    private sealed class FakeHub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? SentBody { get; private set; }
        public string? SentKey { get; private set; }
        public Uri? SentTo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SentTo = request.RequestUri;
            SentKey = request.Headers.TryGetValues(HubWire.KeyHeader, out var v) ? v.Single() : null;
            SentBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Test]
    public async Task Run_PostsTheArgvWithTheKey_AndReturnsTheOperationsExitCode()
    {
        var previous = Environment.GetEnvironmentVariable(HubWire.KeyEnvVar);
        Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, "test-key");
        try
        {
            var hub = new FakeHub(HttpStatusCode.OK, """{"operation":"monitor status","status":"Ok","exit_code":0,"text":"Monitor:  running"}""");
            var output = new StringWriter();
            var error = new StringWriter();

            var code = await MonitorForwarding.RunAsync(MonitorForwarding.Translate(["status"], out _)!, output, error, hub);

            Assert.That(code, Is.Zero);
            Assert.That(output.ToString().Trim(), Is.EqualTo("Monitor:  running"));
            Assert.That(error.ToString(), Does.Contain("forwarded to the Hub: idiotproof monitor status"));
            Assert.That(hub.SentKey, Is.EqualTo("test-key"));
            Assert.That(hub.SentTo!.AbsolutePath, Is.EqualTo(HubWire.OperationPath));
            var sent = JsonDocument.Parse(hub.SentBody!).RootElement;
            Assert.That(sent.GetProperty("argv").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "monitor", "status" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, previous);
        }
    }

    [Test]
    public async Task Run_HubRefuses_IsExit1WithTheReason()
    {
        var previous = Environment.GetEnvironmentVariable(HubWire.KeyEnvVar);
        Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, "wrong");
        try
        {
            var error = new StringWriter();
            var code = await MonitorForwarding.RunAsync(MonitorForwarding.Translate(["status"], out _)!, new StringWriter(), error,
                new FakeHub(HttpStatusCode.Unauthorized, """{"error":"hub_key_required"}"""));

            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("401").And.Contain("hub_key_required"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(HubWire.KeyEnvVar, previous);
        }
    }
}
