using System.Net;
using System.Net.Sockets;
using System.Text;
using IdiotProof.Operations.Hosting;

namespace IdiotProof.Operations.Tests.Hosting;

/// <summary>IP-T77: a second Hub on a taken port is detected before it builds and told who is there.</summary>
public class HubInstanceGuardTests
{
    private sealed class HealthHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    [Test]
    public void IsPortInUse_TrueWhileAnotherListenerHoldsIt_FalseAfter()
    {
        var first = new TcpListener(IPAddress.Loopback, 0);   // "the first Hub"
        first.Start();
        var port = ((IPEndPoint)first.LocalEndpoint).Port;
        try
        {
            Assert.That(HubInstanceGuard.IsPortInUse(port), Is.True);
        }
        finally
        {
            first.Stop();
        }
        Assert.That(HubInstanceGuard.IsPortInUse(port), Is.False);
    }

    [Test]
    public async Task Probe_ReadsTheRunningHubsHealth_AndTheMessageNamesIt()
    {
        var json = """{"status":"ok","version":"1.2.3","started_et":"2026-10-30T21:40:00-04:00","monitor_hosted":true,"database":"x / y","operations":110,"process_id":4242}""";

        var health = await HubInstanceGuard.ProbeAsync(5910, TimeSpan.FromSeconds(1), new HealthHandler(json));

        Assert.That(health, Is.Not.Null);
        var message = HubInstanceGuard.PortTakenMessage(5910, health);
        Assert.That(message, Does.Contain("Another IdiotProof Hub is already running on port 5910 (PID 4242, version 1.2.3, started 2026-10-30 9:40 PM ET, Monitor hosted)"));
        Assert.That(message, Does.Contain("IDIOTPROOF_HUB_PORT"));
    }

    [Test]
    public async Task Probe_SomethingElseOnThePort_IsNotAHub()
    {
        var health = await HubInstanceGuard.ProbeAsync(5910, TimeSpan.FromSeconds(1), new HealthHandler("<html>not a hub</html>"));

        Assert.That(health, Is.Null);
        Assert.That(HubInstanceGuard.PortTakenMessage(5910, null), Does.Contain("does not answer like an IdiotProof Hub"));
    }
}
