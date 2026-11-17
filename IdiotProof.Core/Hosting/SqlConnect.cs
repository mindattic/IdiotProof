using System.Globalization;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.Hosting;

/// <summary>The database a caller needs does not exist on a server where the app must not create it (Azure SQL Database).</summary>
public sealed class DatabaseNotProvisionedException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>
/// How long a first connection waits for a database that is waking up (IP-T542). An Azure SQL serverless database
/// auto-pauses when idle and refuses logins for up to about a minute while it resumes. Read from configuration
/// section <c>Database:ConnectRetry</c> (env <c>Database__ConnectRetry__MaxAttempts</c> and so on); the defaults
/// wait about 90 seconds in total.
/// </summary>
public sealed record SqlConnectRetryOptions
{
    public const string Section = "Database:ConnectRetry";

    /// <summary>Opens tried in all, the first included. 1 turns retrying off.</summary>
    public int MaxAttempts { get; init; } = 8;

    /// <summary>Wait after the first refusal; each later wait is one step longer, up to <see cref="MaxDelaySeconds"/>.</summary>
    public double FirstDelaySeconds { get; init; } = 5;

    public double MaxDelaySeconds { get; init; } = 15;

    /// <summary>
    /// The SQL error numbers that mean "not available yet, try again": 40613 (database not currently available, the
    /// answer of a paused serverless database), 40197 and 40501 (service busy or reconfiguring), 49918 to 49920
    /// (not enough resources to process the request). None of them is raised by a local SQL Server or LocalDB, so
    /// nothing is retried there.
    /// </summary>
    public IReadOnlySet<int> ErrorNumbers { get; init; } = new HashSet<int> { 40613, 40197, 40501, 49918, 49919, 49920 };

    public static SqlConnectRetryOptions From(IConfiguration? configuration)
    {
        var defaults = new SqlConnectRetryOptions();
        var section = configuration?.GetSection(Section);
        if (section is null) return defaults;
        var numbers = (section["ErrorNumbers"] ?? "")
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
            .Where(n => n is not null).Select(n => n!.Value).ToHashSet();
        return new SqlConnectRetryOptions
        {
            MaxAttempts = int.TryParse(section["MaxAttempts"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) && a >= 1 ? a : defaults.MaxAttempts,
            FirstDelaySeconds = double.TryParse(section["FirstDelaySeconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 0 ? f : defaults.FirstDelaySeconds,
            MaxDelaySeconds = double.TryParse(section["MaxDelaySeconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m >= 0 ? m : defaults.MaxDelaySeconds,
            ErrorNumbers = numbers.Count > 0 ? numbers : defaults.ErrorNumbers,
        };
    }
}

/// <summary>
/// Opening SQL connections the same way everywhere (IP-T542), so the app runs on a local SQL Server and on Azure
/// SQL Database alike. Only the open is ever retried, never a command: a command may already have taken a lock,
/// claimed a queue row or written, and the leader lease and the UPDLOCK / READPAST queues depend on that happening
/// exactly once. The connect timeout is whatever the connection string says.
/// </summary>
public static class SqlConnect
{
    /// <summary>SERVERPROPERTY('EngineEdition') of Azure SQL Database.</summary>
    public const int AzureSqlDatabaseEngineEdition = 5;

    /// <summary>Login refusals that can mean "that database is not there" (4060 cannot open database, 18456 login failed, 40532 cannot open server).</summary>
    private static readonly HashSet<int> CannotOpenDatabase = [4060, 18456, 40532];

    private static SqlConnectRetryOptions retry = new();

    /// <summary>Connection strings whose database has been opened by this process, so the existence check runs once each.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> known = new(StringComparer.Ordinal);

    /// <summary>The retry policy in force. Hosts set it once from configuration (<see cref="Configure"/>).</summary>
    public static SqlConnectRetryOptions Retry => Volatile.Read(ref retry);

    public static void Configure(IConfiguration? configuration) => Volatile.Write(ref retry, SqlConnectRetryOptions.From(configuration));

    /// <summary>True when the failure is one of the "not available yet" errors in <see cref="SqlConnectRetryOptions.ErrorNumbers"/>.</summary>
    public static bool IsWaking(SqlException ex, SqlConnectRetryOptions? options = null)
    {
        var numbers = (options ?? Retry).ErrorNumbers;
        if (numbers.Contains(ex.Number)) return true;
        foreach (SqlError e in ex.Errors) if (numbers.Contains(e.Number)) return true;
        return false;
    }

    /// <summary>
    /// Opens the connection, waiting out a database that is resuming. Any other failure, and the last refusal once
    /// the attempts are spent, is thrown as it came.
    /// </summary>
    public static async Task OpenAsync(SqlConnection connection, CancellationToken ct, SqlConnectRetryOptions? options = null)
    {
        var o = options ?? Retry;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (SqlException ex) when (attempt < o.MaxAttempts && IsWaking(ex, o))
            {
                var seconds = Math.Min(o.FirstDelaySeconds * attempt, Math.Max(o.FirstDelaySeconds, o.MaxDelaySeconds));
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A new open connection (see <see cref="OpenAsync(SqlConnection, CancellationToken, SqlConnectRetryOptions?)"/>).</summary>
    public static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var c = new SqlConnection(connectionString);
        try
        {
            await OpenAsync(c, ct).ConfigureAwait(false);
            return c;
        }
        catch
        {
            await c.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<bool> IsAzureSqlDatabaseAsync(SqlConnection open, CancellationToken ct)
    {
        await using var q = new SqlCommand("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)", open);
        return Convert.ToInt32(await q.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) == AzureSqlDatabaseEngineEdition;
    }

    /// <summary>
    /// Whether the database the connection string names is there, waking it if it is paused. False only on a local
    /// server where it is missing, so the caller may create it as before. On Azure SQL Database a missing database
    /// throws <see cref="DatabaseNotProvisionedException"/>: the app never creates one there, because CREATE DATABASE
    /// from the app would need rights on <c>master</c> and would pick a service tier (and a monthly cost) nobody chose.
    /// </summary>
    public static async Task<bool> DatabaseExistsAsync(string connectionString, string purpose, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException($"The {purpose} connection string names no database.");
        var server = builder.DataSource;

        if (known.ContainsKey(connectionString)) return true;

        // Unpooled: a refused login on a pooled connection string makes the pool refuse every open for the next
        // seconds (the pool blocking period), which would fail the open that follows a local CREATE DATABASE.
        SqlException refused;
        try
        {
            await using var c = await OpenAsync(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString, ct).ConfigureAwait(false);
            known[connectionString] = true;
            return true;
        }
        catch (SqlException ex) when (CannotOpenDatabase.Contains(ex.Number))
        {
            refused = ex;
        }

        // The login was refused for this database. Ask master whether it exists and what kind of server this is.
        builder.InitialCatalog = "master";
        bool azure, exists;
        try
        {
            await using var master = await OpenAsync(builder.ConnectionString, ct).ConfigureAwait(false);
            azure = await IsAzureSqlDatabaseAsync(master, ct).ConfigureAwait(false);
            await using var q = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @n", master);
            q.Parameters.AddWithValue("@n", name);
            exists = Convert.ToInt32(await q.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
        }
        catch (SqlException) when (LooksLikeAzure(server))
        {
            // A database-scoped Entra user cannot open master, so it cannot be told apart from "missing" here.
            throw new DatabaseNotProvisionedException(NotProvisioned(purpose, name, server, refused.Message), refused);
        }
        catch (SqlException)
        {
            throw refused;
        }

        if (exists) throw refused;
        if (azure) throw new DatabaseNotProvisionedException(NotProvisioned(purpose, name, server, refused.Message), refused);
        return false;
    }

    private static bool LooksLikeAzure(string server) => server.Contains(".database.", StringComparison.OrdinalIgnoreCase);

    private static string NotProvisioned(string purpose, string database, string server, string reason) =>
        $"The {purpose} database '{database}' cannot be opened on {server} ({reason.ReplaceLineEndings(" ")}). " +
        "IdiotProof does not create databases on Azure SQL Database: provision it first (create the database at the service tier you want " +
        "and add this login as a database user with rights to create tables), then run this again.";

    /// <summary>The connection string with any password taken out, for status output and logs. One without a password comes back unchanged.</summary>
    public static string WithoutSecrets(string connectionString)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(b.Password)) return connectionString;
            b.Remove("Password");
            return b.ConnectionString + ";Password=***";
        }
        catch (ArgumentException)
        {
            return "(unparseable connection string)";
        }
    }
}

/// <summary>
/// Connection strings of the research databases (IP-T542). A study names its own database
/// (<c>IdiotProof_Research_GAP</c>, <c>IdiotProof_Research_T375</c>, ...), so one template carries the server and
/// the credentials and the database name is put in per run: env <c>ConnectionStrings__IdiotProofResearch</c>, then
/// configuration <c>ConnectionStrings:IdiotProofResearch</c>, then LocalDB. Whatever database the template names
/// is replaced.
/// </summary>
public static class ResearchDatabase
{
    public const string EnvVar = "ConnectionStrings__IdiotProofResearch";
    public const string ConfigurationName = "IdiotProofResearch";
    public const string DefaultTemplate =
        @"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof_Research;Trusted_Connection=True;TrustServerCertificate=True;Command Timeout=600;";

    public static string Template(IConfiguration? configuration) =>
        Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } env ? env
        : configuration?.GetConnectionString(ConfigurationName) is { Length: > 0 } configured ? configured
        : DefaultTemplate;

    /// <summary>The template with <paramref name="database"/> as its database.</summary>
    public static string ConnectionStringFor(string database, IConfiguration? configuration)
    {
        if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("A research database name is required.", nameof(database));
        return new SqlConnectionStringBuilder(Template(configuration)) { InitialCatalog = database }.ConnectionString;
    }
}
