using IdiotProof.Operations.Rendering;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Monitor;

[Operation("monitor status", Summary = "Is the Monitor trading? Hosted, lease, last tick, paused, window, active strategies.", Risk = OperationRisk.Read,
    Description = "Reads the Hub's in-process Monitor state, the SettingsKv pause flag, the SQL leader lease and the heartbeat file. Times are ET.")]
[Example("idiotproof monitor status")]
[Example("idiotproof monitor status --json")]
public sealed class MonitorStatusOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var now = ctx.UtcNow;
        var runtime = MonitorReads.Runtime(ctx);
        var (paused, reason) = await MonitorReads.PauseFlag(ctx).ReadAsync(ctx.CancellationToken);
        var lease = await MonitorReads.LeaseAsync(ctx);
        var heartbeat = MonitorReads.Heartbeat(ctx);
        var health = MonitorReads.Classify(runtime, paused, now);

        int? active = null, holding = null;
        if (ctx.Services.GetService<StrategyRepository>() is { } strategies)
        {
            var rows = await strategies.GetActiveAsync(ctx.CancellationToken);
            active = rows.Count;
            holding = rows.Count(s => s.PositionQty != 0);
        }

        var data = new
        {
            Health = health.ToString(),
            Hosted = runtime is not null,
            Running = runtime?.Running ?? false,
            LeaseHeldHere = runtime?.LeaseHeld ?? false,
            Paused = paused,
            PausedReason = reason.Length > 0 ? reason : null,
            runtime?.Window,
            runtime?.Feed,
            runtime?.IntervalSeconds,
            runtime?.StartedUtc,
            runtime?.LastTickUtc,
            runtime?.LastTickSuccessUtc,
            Ticks = runtime?.Ticks,
            FailedTicks = runtime?.FailedTicks,
            runtime?.ConsecutiveFailures,
            runtime?.LastError,
            ActiveStrategies = active,
            OpenPositions = holding,
            Lease = lease,
            Heartbeat = heartbeat,
        };

        var block = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Monitor", MonitorReads.Word(health))
            .Add("Leader lease", runtime?.LeaseHeld == true ? $"held by this Hub (PID {Environment.ProcessId}) since {Fmt.Et(runtime.LeaseAcquiredUtc)}" : DescribeLease(lease))
            .Add("Paused", paused ? $"yes{(reason.Length > 0 ? " - " + reason : "")}" : "no")
            .Add("Window", runtime?.Window)
            .Add("Feed", runtime is null ? null : $"{runtime.Feed}, tick every {runtime.IntervalSeconds:0.#}s")
            .Add("Started", runtime?.StartedUtc is { } st ? Fmt.Et(st) : null)
            .Add("Last tick", runtime is null ? null : $"{Fmt.Et(runtime.LastTickUtc)} ({MonitorReads.Ago(runtime.LastTickUtc, now)})")
            .Add("Ticks", runtime is null ? null : $"{runtime.Ticks:N0} ({runtime.FailedTicks:N0} failed, {runtime.ConsecutiveFailures} in a row)")
            .Add("Last error", runtime?.ConsecutiveFailures > 0 ? runtime.LastError : null)
            .Add("Heartbeat", heartbeat is null ? "no heartbeat file yet" : $"{heartbeat.Status} at {Fmt.Et(heartbeat.WrittenUtc)} ({MonitorReads.Ago(heartbeat.WrittenUtc, now)})")
            .Add("Strategies", active is null ? null : $"{active} active, {holding} holding a position");
        return OperationResult.Ok(data, block.Render());
    }

    internal static string DescribeLease(IdiotProof.Core.Monitoring.MonitorLeaseInfo lease)
    {
        if (!lease.Held) return $"free{(lease.Note is { Length: > 0 } n && n != "no session holds the lease" ? $" ({n})" : "")}";
        var who = lease.HostProcessId is { } pid ? $"PID {pid}" : "another process";
        if (lease.HostName is { Length: > 0 } host) who += $" on {host}";
        if (lease.ProgramName is { Length: > 0 } program) who += $" ({program.Trim()})";
        return $"held by {who}" + (lease.LoginUtc is { } since ? $" since {Fmt.Et(since)}" : "");
    }
}

[Operation("monitor heartbeat", Summary = "The Monitor loop's last heartbeat: when it ticked, ok or fail, failures in a row.", Risk = OperationRisk.Read,
    Description = "Reads the SupervisedLoop heartbeat file (Logs\\monitor.heartbeat), which the loop rewrites after every tick, hosted here or not. Exit 3 (blocked) when it is stale.")]
[Example("idiotproof monitor heartbeat")]
public sealed class MonitorHeartbeatOperation : IOperation<NoArgs>
{
    public Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var now = ctx.UtcNow;
        var path = MonitorReads.HeartbeatPath(ctx);
        var heartbeat = MonitorReads.Heartbeat(ctx);
        if (heartbeat is null)
            return Task.FromResult(OperationResult.Blocked(
                $"No heartbeat yet ({path ?? "no storage configured"}) - the Monitor has not ticked on this machine. `idiotproof monitor status` shows whether it is hosted.",
                new { Path = path }));

        var runtime = MonitorReads.Runtime(ctx);
        var age = now - heartbeat.WrittenUtc;
        var stale = age > MonitorReads.StaleAfter(runtime);
        var data = new { Path = path, heartbeat.WrittenUtc, heartbeat.TickStartUtc, heartbeat.Status, heartbeat.ConsecutiveFailures, heartbeat.Error, AgeSeconds = (int)age.TotalSeconds, Stale = stale };
        var text = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Last beat", $"{Fmt.Et(heartbeat.WrittenUtc)} ({MonitorReads.Ago(heartbeat.WrittenUtc, now)}){(stale ? "  STALE" : "")}")
            .Add("Status", heartbeat.Status)
            .Add("Failures", heartbeat.ConsecutiveFailures > 0 ? $"{heartbeat.ConsecutiveFailures} in a row" : "none")
            .Add("Error", heartbeat.Error)
            .Add("File", path)
            .Render();
        return Task.FromResult(stale
            ? OperationResult.Blocked($"The Monitor's heartbeat is stale - last beat {MonitorReads.Ago(heartbeat.WrittenUtc, now)}.\n{text}", data)
            : OperationResult.Ok(data, text));
    }
}

[Operation("monitor lease", Summary = "Who holds the Monitor's SQL leader lease (only the holder evaluates and trades).", Risk = OperationRisk.Read,
    Description = "One Monitor per database trades: it holds sp_getapplock 'IdiotProof.Monitor.Leader'. Shows the holder's PID and machine when SQL Server reports them.")]
[Example("idiotproof monitor lease")]
public sealed class MonitorLeaseOperation : IOperation<NoArgs>
{
    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var runtime = MonitorReads.Runtime(ctx);
        var lease = await MonitorReads.LeaseAsync(ctx);
        var heldHere = runtime?.LeaseHeld == true || (lease.HostProcessId is { } pid && pid == Environment.ProcessId);
        var data = new { lease.Held, HeldByThisHub = heldHere, HubProcessId = Environment.ProcessId, Lease = lease, AcquiredUtc = runtime?.LeaseAcquiredUtc };

        var text = new KeyValueBlock { Ansi = Ansi.For(ctx) }
            .Add("Lease", heldHere ? $"held by this Hub (PID {Environment.ProcessId})" : MonitorStatusOperation.DescribeLease(lease))
            .Add("Since", heldHere && runtime?.LeaseAcquiredUtc is { } at ? Fmt.Et(at) : null)
            .Add("Session", lease.SessionId?.ToString())
            .Add("Source", lease.Note)
            .Add("Meaning", lease.Held
                ? heldHere ? "this Hub's Monitor is the one evaluating and trading" : "another process is the trading Monitor; this Hub's Monitor (if hosted) is on standby"
                : "no Monitor is trading against this database right now")
            .Render();
        return OperationResult.Ok(data, text);
    }
}
