using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using IdiotProof.Core.Hosting;
using IdiotProof.Core.Monitoring;
using IdiotProof.Operations.Monitor;
using IdiotProof.Operations.Rendering;
namespace IdiotProof.Operations.BuiltIn;

/// <summary>
/// Liveness check for the Hub: proves the dispatcher, the DI scope and the clock all work and
/// shows the Hub's notion of "now" in ET (a skewed clock is worth knowing before a 9:30 entry).
/// It also probes the IdiotProof DB directly (3-second budget) and reports the hosted Monitor's
/// last good tick, so "pong" can no longer hide a dead database or a failing Monitor (IP-T359).
/// Exit stays 0 either way: the Hub itself answered; <c>healthy</c> says whether it can trade.
/// </summary>
[Operation("hub ping", Summary = "Check the Hub is up, show its clock in ET, and whether its DB and Monitor are healthy.", Risk = OperationRisk.Read)]
[Example("idiotproof hub ping")]
public sealed class HubPingOperation : IOperation<NoArgs>
{
    private const int ProbeSeconds = 3;

    private static readonly string Version =
        typeof(HubPingOperation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HubPingOperation).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public async Task<OperationResult> ExecuteAsync(NoArgs args, OperationContext ctx)
    {
        var now = ctx.UtcNow;
        var db = ctx.Services.GetService<IdiotProofDatabase>() is { } database
            ? await ProbeAsync(database.ConnectionString, ctx.CancellationToken)
            : null;
        var runtime = MonitorReads.Runtime(ctx);
        var outage = runtime is null ? null : MonitorOutageWatchdog.Assess(runtime, now);
        var healthy = db?.Ok != false && outage is null;

        var lines = new List<string> { healthy ? $"pong — {ctx.Et(now)}" : $"pong — {ctx.Et(now)} — DEGRADED" };
        if (db is not null)
            lines.Add(db.Ok ? $"  db: ok ({db.Ms} ms)" : $"  db: UNREACHABLE after {db.Ms} ms — {db.Error}");
        if (runtime is not null)
            lines.Add(outage is not null
                ? $"  monitor: NOT EVALUATING — {outage}; last good tick {Fmt.Et(runtime.LastTickSuccessUtc)} ({MonitorReads.Ago(runtime.LastTickSuccessUtc, now)})"
                : $"  monitor: {MonitorReads.Word(MonitorReads.Classify(runtime, paused: false, now))}; last good tick {Fmt.Et(runtime.LastTickSuccessUtc)} ({MonitorReads.Ago(runtime.LastTickSuccessUtc, now)})");

        return OperationResult.Ok(
            new
            {
                Pong = true,
                Now = now,
                Version,
                Caller = ctx.Caller,
                Healthy = healthy,
                Db = db,
                Monitor = runtime is null ? null : new
                {
                    runtime.Running,
                    runtime.LeaseHeld,
                    runtime.LastTickSuccessUtc,
                    runtime.ConsecutiveFailures,
                    runtime.LastError,
                    Outage = outage,
                },
            },
            string.Join(Environment.NewLine, lines));
    }

    private static async Task<DbProbe> ProbeAsync(string connectionString, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = ProbeSeconds, Pooling = false };
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(ProbeSeconds * 2));
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(budget.Token);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DB_NAME();";
            cmd.CommandTimeout = ProbeSeconds;
            var name = await cmd.ExecuteScalarAsync(budget.Token) as string;
            return new DbProbe(true, sw.ElapsedMilliseconds, name, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var message = ex is OperationCanceledException ? $"no answer within {ProbeSeconds * 2}s" : ex.Message.ReplaceLineEndings(" ");
            return new DbProbe(false, sw.ElapsedMilliseconds, null, message.Length <= 160 ? message : message[..157] + "...");
        }
    }

    private sealed record DbProbe(bool Ok, long Ms, string? Database, string? Error);
}
