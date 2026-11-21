using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Monitoring;

/// <summary>Keeps the machine from idle-sleeping while held (IP-T624).</summary>
public interface ISystemSleepBlocker
{
    bool Acquire(string reason);
    void Release();
}

/// <summary>
/// Holds the system awake while the Hub hosts the Monitor (IP-T624). On 2026-11-20 the box idle-slept at 08:59:50 ET
/// for 6 min 22 s in the premarket window: every SQL call hung until wake, the leader lease's session was gone, and the
/// Monitor stopped trading. A Windows power request (the same mechanism a media player uses) stops idle sleep for as
/// long as this process lives; it does not override a user-initiated sleep or a lid close.
/// </summary>
public sealed class MonitorKeepAwake(ISystemSleepBlocker blocker, ILogger<MonitorKeepAwake> logger) : IHostedService
{
    public const string Reason = "IdiotProof Hub: the Monitor is trading unattended";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (blocker.Acquire(Reason))
            logger.LogInformation("Holding the system awake while the Monitor is hosted.");
        else
            logger.LogWarning("Could not hold the system awake: idle sleep will stop the Monitor (set the power plan to never sleep).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        blocker.Release();
        return Task.CompletedTask;
    }
}

/// <summary><c>PowerCreateRequest</c> + <c>PowerSetRequest(PowerRequestSystemRequired)</c>; a no-op off Windows.</summary>
public sealed class WindowsSystemSleepBlocker : ISystemSleepBlocker, IDisposable
{
    private const int PowerRequestSystemRequired = 1;
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 0x1;
    private static readonly IntPtr InvalidHandle = new(-1);

    private readonly object gate = new();
    private IntPtr handle = IntPtr.Zero;

    public bool IsHeld
    {
        get { lock (gate) return handle != IntPtr.Zero; }
    }

    public bool Acquire(string reason)
    {
        if (!OperatingSystem.IsWindows()) return false;
        lock (gate)
        {
            if (handle != IntPtr.Zero) return true;
            var context = new ReasonContext { Version = PowerRequestContextVersion, Flags = PowerRequestContextSimpleString, SimpleReasonString = reason };
            var created = PowerCreateRequest(ref context);
            if (created == IntPtr.Zero || created == InvalidHandle) return false;
            if (!PowerSetRequest(created, PowerRequestSystemRequired))
            {
                CloseHandle(created);
                return false;
            }
            handle = created;
            return true;
        }
    }

    public void Release()
    {
        lock (gate)
        {
            if (handle == IntPtr.Zero) return;
            PowerClearRequest(handle, PowerRequestSystemRequired);
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }

    public void Dispose() => Release();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerSetRequest(IntPtr request, int requestType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerClearRequest(IntPtr request, int requestType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
