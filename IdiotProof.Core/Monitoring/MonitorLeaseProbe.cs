using Microsoft.Data.SqlClient;

namespace IdiotProof.Monitor;

/// <summary>Who holds the Monitor's SQL leader lease, as seen from outside the lease's own session.</summary>
public sealed record MonitorLeaseInfo
{
    /// <summary>True when some session holds the lease (a Monitor is trading against this database).</summary>
    public bool Held { get; init; }

    /// <summary>SQL session id of the holder, when SQL Server would say (VIEW SERVER STATE).</summary>
    public int? SessionId { get; init; }

    /// <summary>Client machine name of the holder's session.</summary>
    public string? HostName { get; init; }

    /// <summary>Windows process id of the holder (compare with the Hub's PID).</summary>
    public int? HostProcessId { get; init; }

    /// <summary>Program name the holder's connection reported.</summary>
    public string? ProgramName { get; init; }

    /// <summary>When the holder's session logged in (UTC) — roughly when it took the lease.</summary>
    public DateTime? LoginUtc { get; init; }

    /// <summary>How the answer was obtained, or why it could not be.</summary>
    public string? Note { get; init; }
}

/// <summary>Reads the leader-lease state (<c>monitor lease</c>, IP-T256). A seam so operations test without SQL.</summary>
public interface IMonitorLeaseProbe
{
    Task<MonitorLeaseInfo> ProbeAsync(CancellationToken ct = default);
}

/// <summary>
/// The SQL Server probe: <c>sys.dm_tran_locks</c> joined to <c>sys.dm_exec_sessions</c> names the
/// holder (needs VIEW SERVER STATE, which a LocalDB owner has); without that permission it falls
/// back to <c>APPLOCK_TEST</c>, which says only whether the lease is free. Never takes the lease.
/// </summary>
public sealed class SqlMonitorLeaseProbe(IdiotProof.Core.Hosting.IdiotProofDatabase database) : IMonitorLeaseProbe
{
    public async Task<MonitorLeaseInfo> ProbeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(database.ConnectionString);
        await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT TOP 1 s.session_id, s.host_name, s.host_process_id, s.program_name, s.login_time
                FROM sys.dm_tran_locks l
                JOIN sys.dm_exec_sessions s ON s.session_id = l.request_session_id
                WHERE l.resource_type = 'APPLICATION'
                  AND l.resource_database_id = DB_ID()
                  AND l.request_mode = 'X'
                  AND l.resource_description LIKE '%' + @res + '%';
                """;
            cmd.Parameters.AddWithValue("@res", MonitorLeaderLease.Resource);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return new MonitorLeaseInfo { Held = false, Note = "no session holds the lease" };
            return new MonitorLeaseInfo
            {
                Held = true,
                SessionId = reader.GetInt16(0),
                HostName = reader.IsDBNull(1) ? null : reader.GetString(1),
                HostProcessId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ProgramName = reader.IsDBNull(3) ? null : reader.GetString(3),
                LoginUtc = reader.IsDBNull(4) ? null : DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Local).ToUniversalTime(),
                Note = "sys.dm_tran_locks",
            };
        }
        catch (SqlException)
        {
            // No VIEW SERVER STATE: fall through to the permission-free test.
        }

        await using var test = conn.CreateCommand();
        test.CommandText = "SELECT APPLOCK_TEST('public', @res, 'Exclusive', 'Session');";
        test.Parameters.AddWithValue("@res", MonitorLeaderLease.Resource);
        var free = Convert.ToInt32(await test.ExecuteScalarAsync(ct)) == 1;
        return new MonitorLeaseInfo { Held = !free, Note = "APPLOCK_TEST (holder unknown without VIEW SERVER STATE)" };
    }
}
