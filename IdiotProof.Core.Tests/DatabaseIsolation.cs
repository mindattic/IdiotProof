// No namespace on purpose: a [SetUpFixture] covers only its own namespace, so this one covers the whole assembly.
/// <summary>
/// IP-T545: with nothing configured the app resolves its databases to the real Azure SQL ones
/// (<see cref="IdiotProof.Core.Hosting.CloudDatabases"/>), and a developer's or the Hub service's machine may carry
/// the real connection strings in its environment. For the whole test run the three variables are pinned to a
/// server that cannot answer, so a test that builds the app's service graph can never open, let alone write, a real
/// database. Fixtures that need a real SQL Server get one from <see cref="IdiotProof.Core.Tests.TestSqlServer"/> only.
/// </summary>
[SetUpFixture]
public sealed class IdiotProofDatabaseIsolation
{
    /// <summary>Nothing listens on loopback port 1, and the name is not a real database's.</summary>
    public const string Nowhere = "Server=tcp:127.0.0.1,1;Database=IdiotProof_Test_Unconfigured;Connect Timeout=1;Encrypt=False;";

    private static readonly string[] Variables =
    [
        "ConnectionStrings__IdiotProof",
        IdiotProof.Core.History.HistoryDatabase.EnvVar,
        IdiotProof.Core.Hosting.ResearchDatabase.EnvVar,
    ];

    private readonly Dictionary<string, string?> saved = [];

    [OneTimeSetUp]
    public void Isolate()
    {
        foreach (var name in Variables)
        {
            saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, Nowhere);
        }
    }

    [OneTimeTearDown]
    public void Restore()
    {
        foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
    }
}
