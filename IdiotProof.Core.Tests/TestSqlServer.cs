using IdiotProof.Core.Hosting;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.Tests;

/// <summary>
/// The one place a test gets a real SQL Server from (IP-T545). Developing IdiotProof needs no local SQL Server, so
/// the fixtures that need a real one (EF relational behaviour, <c>sp_getapplock</c>, columnstore) are opt-in: set
/// the environment variable <see cref="EnvVar"/> to a server-level connection string and they run; leave it unset
/// and each of them is skipped with a message naming the variable. They share the category <see cref="Category"/>.
/// <para>
/// Each fixture still makes its own scratch database (<c>IdiotProof_Test_{guid}</c> and the like) on that server and
/// drops it afterwards, so the server must let the login create and drop databases: a container, a spare instance,
/// LocalDB. On Azure SQL a created database is billed at the server's default tier until it is dropped, so point
/// this at an elastic pool or a throwaway server, not at the trading server.
/// </para>
/// <para>
/// It never falls back to the app's own connection resolution (<see cref="CloudDatabases"/>), and it refuses any
/// database named like a real one (<see cref="CloudDatabases.IsRealDatabaseName"/>): <c>IdiotProof</c>,
/// <c>IdiotProof_History</c>, <c>IdiotProof_Research</c> and <c>IdiotProof_Research_*</c>, on any server.
/// </para>
/// </summary>
public static class TestSqlServer
{
    /// <summary>A server-level connection string (its database is ignored, <c>master</c> is fine), e.g. <c>Server=localhost,14333;User Id=sa;Password=...;TrustServerCertificate=True;</c>.</summary>
    public const string EnvVar = "IDIOTPROOF_TEST_SQLSERVER";

    /// <summary>NUnit category of every fixture that needs a real SQL Server.</summary>
    public const string Category = "SqlServer";

    private static string? Raw => Environment.GetEnvironmentVariable(EnvVar) is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>Whether the opt-in variable is set. Teardowns ask this so a skipped fixture cleans up nothing.</summary>
    public static bool IsConfigured => Raw is not null;

    /// <summary>
    /// Call first in a fixture's set-up: skips the fixture (<c>Assert.Ignore</c>) when the variable is unset, and
    /// throws when it is set to something these tests must not use.
    /// </summary>
    public static void Require()
    {
        if (Raw is null)
            Assert.Ignore($"Needs a real SQL Server. Set the environment variable {EnvVar} to a server-level connection string " +
                          "(a login that may create and drop databases) to run it; no local SQL Server is needed otherwise.");
        _ = Server();
    }

    /// <summary>A scratch database name, <c>{prefix}_{guid}</c>. Safe to call from a static initializer.</summary>
    public static string NewDatabaseName(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        RefuseRealDatabase(name);
        return name;
    }

    /// <summary>The opted-in server with <paramref name="database"/> as its database.</summary>
    public static string ConnectionStringFor(string database, int? connectTimeoutSeconds = null)
    {
        RefuseRealDatabase(database);
        var builder = Server();
        builder.InitialCatalog = database;
        if (connectTimeoutSeconds is { } seconds) builder.ConnectTimeout = seconds;
        return builder.ConnectionString;
    }

    /// <summary>The opted-in server's <c>master</c>, for CREATE DATABASE and DROP DATABASE of a scratch database.</summary>
    public static string Master => ConnectionStringFor("master");

    /// <summary>Drops a scratch database if it is there. Refuses a real database name before it connects.</summary>
    public static async Task DropDatabaseAsync(string database)
    {
        RefuseRealDatabase(database);
        if (!IsConfigured) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(Master);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Throws when <paramref name="database"/> is named like a database that holds real data.</summary>
    public static void RefuseRealDatabase(string? database)
    {
        if (CloudDatabases.IsRealDatabaseName(database))
            throw new InvalidOperationException(
                $"Refusing to run a test against '{database}': that is the name of a real IdiotProof database. Tests use scratch databases only.");
    }

    private static SqlConnectionStringBuilder Server()
    {
        var raw = Raw ?? throw new InvalidOperationException($"{EnvVar} is not set. Call {nameof(TestSqlServer)}.{nameof(Require)}() first, so the fixture is skipped.");
        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(raw); }
        catch (ArgumentException ex) { throw new InvalidOperationException($"{EnvVar} is not a SQL Server connection string: {ex.Message}", ex); }
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException($"{EnvVar} names no server.");
        if (CloudDatabases.IsRealDatabaseName(builder.InitialCatalog))
            throw new InvalidOperationException(
                $"{EnvVar} names the database '{builder.InitialCatalog}', a real IdiotProof database. Give a server-level connection string " +
                "(no database, or master); the tests make and drop their own scratch databases.");
        return builder;
    }
}
