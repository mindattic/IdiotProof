using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace IdiotProof.Operations.Hosting;

/// <summary>
/// One Hub per port (IP-T77). Checked before the Hub builds anything — no migrations, no Monitor,
/// no window title change — so a second <c>hub start</c>, a second service instance or a stray
/// <c>dotnet run</c> exits at once with a message naming the Hub already there instead of an
/// unhandled Kestrel bind exception. Kestrel's own exclusive bind stays the backstop for a race.
/// </summary>
public static class HubInstanceGuard
{
    /// <summary>Exit code for "another Hub already owns the port".</summary>
    public const int PortTakenExitCode = 2;

    /// <summary>True when something already listens on loopback <paramref name="port"/>.</summary>
    public static bool IsPortInUse(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        try
        {
            listener.Start();
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            return true;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>The running Hub's health when the port answers like a Hub, else null (something else owns it).</summary>
    public static async Task<HubHealth?> ProbeAsync(int port, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            http.Timeout = timeout;
            return await http.GetFromJsonAsync<HubHealth>(new Uri($"http://127.0.0.1:{port}{HubWire.HealthPath}"), OperationJson.Options);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>What the second Hub prints before it exits.</summary>
    public static string PortTakenMessage(int port, HubHealth? running) =>
        running is not null
            ? $"Another IdiotProof Hub is already running on port {port} (PID {running.ProcessId}, version {running.Version}, " +
              $"started {running.StartedEt:yyyy-MM-dd h:mm tt} ET, Monitor {(running.MonitorHosted ? "hosted" : "not hosted")}). " +
              $"Use it (`idiotproof hub status`), stop it (`idiotproof hub stop`), or set {HubWire.PortEnvVar} to run a second Hub on another port."
            : $"Port {port} is already in use by another program (it does not answer like an IdiotProof Hub). " +
              $"Free the port or set {HubWire.PortEnvVar} to a different one.";
}
