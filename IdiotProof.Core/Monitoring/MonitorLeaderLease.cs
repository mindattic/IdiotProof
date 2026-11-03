using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using IdiotProof.Core.Services;
using IdiotProof.Core.Data;
namespace IdiotProof.Core.Monitoring;

/// <summary>
/// Single-active-instance lease (IP-LAW-10). Two Monitors evaluating the same
/// Strategies table would double-fire orders and double-write positions, so
/// before the loop starts the worker must hold an exclusive SQL application
/// lock (<c>sp_getapplock</c>, session-owned) on the shared database. The
/// lock lives exactly as long as this connection: if the process dies, SQL
/// Server releases it and a standby instance acquires it on its next retry —
/// a poor-man's leader election with zero extra infrastructure.
/// </summary>
public sealed class MonitorLeaderLease : IAsyncDisposable
{
    /// <summary>The sp_getapplock resource every Monitor competes for.</summary>
    public const string Resource = "IdiotProof.Monitor.Leader";
    private readonly SqlConnection connection;

    private MonitorLeaderLease(SqlConnection connection) => this.connection = connection;

    /// <summary>
    /// Blocks until the lease is acquired (retrying every <paramref name="retryDelay"/>,
    /// default 15s) or the token cancels.
    /// </summary>
    public static async Task<MonitorLeaderLease> AcquireAsync(
        string connectionString,
        ILogger logger,
        CancellationToken ct,
        TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? TimeSpan.FromSeconds(15);
        var announced = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var conn = new SqlConnection(connectionString);
            bool acquired = false;
            try
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, " +
                                  "@LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;";
                cmd.Parameters.AddWithValue("@res", Resource);
                var result = (int)(await cmd.ExecuteScalarAsync(ct) ?? -999);

                if (result >= 0) // 0 = granted, 1 = granted after wait
                {
                    acquired = true;
                    logger.LogInformation("Monitor leader lease acquired — this instance evaluates and trades.");
                    return new MonitorLeaderLease(conn);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Leader-lease attempt failed; retrying in {Delay}s.", delay.TotalSeconds);
            }
            finally
            {
                // Dispose the connection unless we're handing it to the lease object.
                if (!acquired) await conn.DisposeAsync();
            }
            if (!announced)
            {
                logger.LogWarning("Another Monitor instance holds the leader lease — standing by (retry every {Delay}s).", delay.TotalSeconds);
                announced = true;
            }
            await Task.Delay(delay, ct);
        }
    }

    /// <summary>
    /// True only while this connection's session still owns the exclusive applock (IP-T356). The
    /// lock lives with the session, so a dropped connection (network blip, SQL failover, killed
    /// session) silently releases it and a standby may already be leading; callers check this
    /// before every tick and stop trading on false. Any failure to confirm counts as lost.
    /// </summary>
    public async Task<bool> IsHeldAsync(CancellationToken ct)
    {
        // One command at a time on the lease's connection (no MARS): the tick-top check and the
        // gateway's pre-order check (TransactionGateway LeaderCheck) share it.
        try { await probeGate.WaitAsync(ct); }
        catch (ObjectDisposedException) { return false; }
        try
        {
            if (disposed || connection.State != System.Data.ConnectionState.Open) return false;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT APPLOCK_MODE('public', @res, 'Session');";
            cmd.Parameters.AddWithValue("@res", Resource);
            var mode = await cmd.ExecuteScalarAsync(ct) as string;
            return string.Equals(mode, "Exclusive", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            probeGate.Release();
        }
    }

    private readonly SemaphoreSlim probeGate = new(1, 1);
    private volatile bool disposed;

    /// <summary>
    /// Releases the lease explicitly, then closes the connection (IP-T79). Closing alone is not
    /// enough: a pooled connection goes back to the pool with its session — and the session-owned
    /// applock — still alive until the pool reuses or prunes it, so a restarted Hub (or a standby)
    /// could wait minutes for a lease nobody is using.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        disposed = true; // a late IsHeldAsync (e.g. a stale gateway LeaderCheck) now answers "not held"
        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session';";
                cmd.Parameters.AddWithValue("@res", Resource);
                await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Best effort: if the release fails the session still ends below, and SQL Server drops
            // the lock when the pooled session is finally closed or reset.
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}

