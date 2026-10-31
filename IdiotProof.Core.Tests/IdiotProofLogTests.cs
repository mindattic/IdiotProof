using IdiotProof.Core.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace IdiotProof.Core.Tests;

/// <summary>
/// IdiotProofLog (IP-T16/IP-T23): each entry point's MindAttic.Log database at Vault's layout,
/// tagged with its Component, opened lazily, fed by ILogger with EF/framework chatter kept out,
/// and never a reason the caller fails. Every test points the log at a temp file.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class IdiotProofLogTests
{
    private string root = "";
    private string dbPath = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"idiotproof-log-{Guid.NewGuid():N}");
        dbPath = Path.Combine(root, "MindAttic.Log.db");
    }

    [TearDown]
    public void TearDown()
    {
        IdiotProofLog.ResetForTests();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [Test]
    public void DatabasePath_IsVaultsPerApplicationLayout()
    {
        var path = IdiotProofLog.DatabasePath(IdiotProofLog.Blazor, @"C:\Users\u\AppData\Roaming");
        Assert.That(path, Is.EqualTo(Path.Combine(@"C:\Users\u\AppData\Roaming", "MindAttic", "Logging", "IdiotProof.Blazor", "MindAttic.Log.db")));
    }

    [Test]
    public void Start_DoesNotOpenTheDatabaseUntilSomethingIsLogged()
    {
        IdiotProofLog.Start(IdiotProofLog.Cli, dbPath);

        Assert.That(IdiotProofLog.CurrentPath, Is.EqualTo(dbPath));
        Assert.That(File.Exists(dbPath), Is.False, "a CLI run that logs nothing never touches the file");
    }

    [Test]
    public void Error_LandsWithApplicationCategoryComponentAndException()
    {
        IdiotProofLog.Start(IdiotProofLog.Cli, dbPath);
        IdiotProofLog.Error("Cli", new InvalidOperationException("boom"), "{Message:l}", "idiotproof hub start: could not start the Hub");
        IdiotProofLog.Stop();

        var rows = ReadRows();
        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Application, Is.EqualTo("IdiotProof.Cli"));
            Assert.That(row.Category, Is.EqualTo("Cli"));
            Assert.That(row.Level, Is.EqualTo((int)LogEventLevel.Error));
            Assert.That(row.Message, Is.EqualTo("idiotproof hub start: could not start the Hub"));
            Assert.That(row.Exception, Does.Contain("boom"));
            Assert.That(row.Properties, Does.Contain("\"Component\"").And.Contain("Cli"));
        });
    }

    [Test]
    public void EnvironmentVariable_RedirectsTheDatabase()
    {
        var previous = Environment.GetEnvironmentVariable(IdiotProofLog.DbPathEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(IdiotProofLog.DbPathEnvVar, dbPath);
            IdiotProofLog.Start(IdiotProofLog.Mcp);
            Assert.That(IdiotProofLog.CurrentPath, Is.EqualTo(dbPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(IdiotProofLog.DbPathEnvVar, previous);
        }
    }

    [Test]
    public void AddIdiotProofLog_TakesAppInformationButOnlyFrameworkWarnings()
    {
        IdiotProofLog.Start(IdiotProofLog.Monitor, dbPath);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddIdiotProofLog());
        using (var provider = services.BuildServiceProvider())
        {
            var factory = provider.GetRequiredService<ILoggerFactory>();
            factory.CreateLogger("IdiotProof.Core.Monitoring.MonitorWorker").LogInformation("tick {N}", 1);
            factory.CreateLogger("IdiotProof.Core.Monitoring.MonitorWorker").LogDebug("too chatty");
            factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogInformation("SELECT 1");
            factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogWarning("slow query");
        }
        IdiotProofLog.Stop();

        var messages = ReadRows().Select(r => r.Message).ToList();
        Assert.That(messages, Is.EquivalentTo(new[] { "tick 1", "slow query" }));
    }

    [Test]
    public void AddIdiotProofLog_HonorsAHigherMinimum()
    {
        IdiotProofLog.Start(IdiotProofLog.Mcp, dbPath);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddIdiotProofLog(LogLevel.Warning));
        using (var provider = services.BuildServiceProvider())
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("IdiotProof.Mcp");
            logger.LogInformation("hidden");
            logger.LogWarning("kept");
        }
        IdiotProofLog.Stop();

        Assert.That(ReadRows().Select(r => r.Message), Is.EquivalentTo(new[] { "kept" }));
    }

    [Test]
    public void NotStarted_WritesAreNoOps()
    {
        Assert.DoesNotThrow(() => IdiotProofLog.Error("Cli", null, "nothing"));
        Assert.That(IdiotProofLog.Logger, Is.Null);
    }

    [Test]
    public void UnopenableDatabase_IsNotTheCallersProblem()
    {
        // A directory where the file should be: SQLite can't open it.
        Directory.CreateDirectory(dbPath);
        IdiotProofLog.Start(IdiotProofLog.ResearchScanner, dbPath);

        Assert.DoesNotThrow(() => IdiotProofLog.Error("ScanRun", null, "lost"));
        Assert.That(IdiotProofLog.Logger, Is.Null);
    }

    private sealed record Row(string Application, string? Category, int Level, string Message, string? Exception, string? Properties);

    private List<Row> ReadRows()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Application, Category, Level, Message, Exception, PropertiesJson FROM MindAttic_Log ORDER BY Id;";
        using var reader = cmd.ExecuteReader();
        var rows = new List<Row>();
        while (reader.Read())
            rows.Add(new Row(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }
}
