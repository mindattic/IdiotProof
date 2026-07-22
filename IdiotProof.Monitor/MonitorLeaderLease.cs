using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Monitor;

/// <summary>
/// Single-active-instance lease (IP-A9). Two Monitors evaluating the same
/// Strategies table would double-fire orders and double-write positions, so
/// before the loop starts the worker must hold an exclusive SQL application
/// lock (<c>sp_getapplock</c>, session-owned) on the shared database. The
/// lock lives exactly as long as this connection: if the process dies, SQL
/// Server releases it and a standby instance acquires it on its next retry —
/// a poor-man's leader election with zero extra infrastructure.
/// </summary>
public sealed class MonitorLeaderLease : IAsyncDisposable
{
    private const string Resource = "IdiotProof.Monitor.Leader";
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

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

/// <summary>The Monitor's database connection string, surfaced through DI for the lease.</summary>
public sealed record MonitorDatabase(string ConnectionString);
