using IdiotProof.Core.Monitoring;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdiotProof.Core.Tests;

/// <summary>
/// IP-US-K9: two Monitor instances contend for the one SQL leader lease. The second stands by
/// while the first leads and takes over only once the first lets go. Runs on a throwaway LocalDB
/// database (applocks are per database), never the real IdiotProof one.
/// </summary>
[TestFixture]
public sealed class MonitorLeaderLeaseTests
{
    private static readonly string DbName = $"IdiotProof_Lease_{Guid.NewGuid():N}";
    private static readonly string Master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly string ConnStr = $@"Server=(localdb)\MSSQLLocalDB;Database={DbName};Trusted_Connection=True;TrustServerCertificate=True;";
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(100);

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        await using var conn = new SqlConnection(Master);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"CREATE DATABASE [{DbName}]", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(Master);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"ALTER DATABASE [{DbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DbName}]", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task<MonitorLeaderLease> Acquire(CancellationToken ct) =>
        MonitorLeaderLease.AcquireAsync(ConnStr, NullLogger.Instance, ct, Retry);

    [Test]
    public async Task SecondInstance_StandsBy_WhileTheLeaderHoldsTheLease()
    {
        await using var leader = await Acquire(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        Assert.That(async () => await Acquire(cts.Token), Throws.InstanceOf<OperationCanceledException>(),
            "a standby must keep waiting, never acquire a lease that is held");
        Assert.That(await leader.IsHeldAsync(CancellationToken.None), Is.True);
    }

    [Test]
    public async Task Standby_TakesOver_WhenTheLeaderReleases_AndTheOldLeaderNoLongerHoldsIt()
    {
        var leader = await Acquire(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var standby = Acquire(cts.Token);

        await Task.Delay(400);
        Assert.That(standby.IsCompleted, Is.False, "standby waits while the leader is alive");

        await leader.DisposeAsync();
        await using var successor = await standby;

        Assert.Multiple(async () =>
        {
            Assert.That(await successor.IsHeldAsync(CancellationToken.None), Is.True);
            Assert.That(await leader.IsHeldAsync(CancellationToken.None), Is.False, "a released lease never reports held");
        });
    }

    [Test]
    public async Task LeaderSessionKilled_ReleasesTheLease_ToTheStandby()
    {
        var leader = await Acquire(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var standby = Acquire(cts.Token);
        await Task.Delay(300);

        // Simulate the leader process dying: kill its SQL session out from under it.
        await using (var admin = new SqlConnection(ConnStr))
        {
            await admin.OpenAsync();
            await using var find = new SqlCommand(
                "SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_mode = 'X' AND resource_database_id = DB_ID()", admin);
            var spid = Convert.ToInt32(await find.ExecuteScalarAsync());
            await using var kill = new SqlCommand($"KILL {spid}", admin);
            await kill.ExecuteNonQueryAsync();
        }

        await using var successor = await standby;
        Assert.Multiple(async () =>
        {
            Assert.That(await successor.IsHeldAsync(CancellationToken.None), Is.True);
            Assert.That(await leader.IsHeldAsync(CancellationToken.None), Is.False, "the dead leader must stop trading");
        });
        await leader.DisposeAsync();
    }
}
