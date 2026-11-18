using Microsoft.Data.SqlClient;

namespace IdiotProof.Core.History;

/// <summary>
/// The permanent market history (IP-T454): a database of its own (<c>IdiotProof_History</c> on Azure
/// SQL, serverless), because five years of 1-minute bars per ticker is far larger than the trading
/// database should carry. Minute bars live in an ordered clustered columnstore (compressed about tenfold); daily
/// bars, splits, news and filings in rowstore tables; <c>Coverage</c> records every (symbol, kind,
/// day) already fetched so nothing is ever requested twice. The schema is versioned SQL applied in
/// order, because a columnstore table without a key is outside what EF migrations create.
/// <para>
/// The connection string is env <c>ConnectionStrings__IdiotProofHistory</c>, then configuration
/// <c>ConnectionStrings:IdiotProofHistory</c>, then <see cref="IdiotProof.Core.Hosting.CloudDatabases.History"/> (IP-T545; never a local instance). On Azure SQL Database (IP-T542) the database
/// must already exist: it is never created from here, and a paused serverless database is waited for on open.
/// </para>
/// </summary>
public sealed class HistoryDatabase(string connectionString)
{
    public const string EnvVar = "ConnectionStrings__IdiotProofHistory";
    public const string DefaultConnectionString = IdiotProof.Core.Hosting.CloudDatabases.History;

    public string ConnectionString => connectionString;

    public static HistoryDatabase FromEnvironment(string? configured = null) =>
        new(Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } env ? env : string.IsNullOrWhiteSpace(configured) ? DefaultConnectionString : configured);

    public string DatabaseName => new SqlConnectionStringBuilder(connectionString).InitialCatalog;

    /// <summary>A new open connection. Only the open is retried (a database resuming from auto-pause); a command never is.</summary>
    public Task<SqlConnection> OpenAsync(CancellationToken ct) => IdiotProof.Core.Hosting.SqlConnect.OpenAsync(connectionString, ct);

    /// <summary>Kinds of coverage marks.</summary>
    public static class Kinds
    {
        public const string Minute = "1Min";
        public const string Daily = "1Day";
        public const string News = "news";
        public const string Filings = "filings";
        public const string Splits = "splits";
    }

    /// <summary>The schema steps, applied once each in order and recorded in <c>SchemaVersion</c>.</summary>
    public static readonly IReadOnlyList<(int Version, string Name, string Sql)> Steps =
    [
        (1, "minute bars, daily bars, coverage, splits", """
            CREATE TABLE dbo.MinuteBars (
                Symbol varchar(16) NOT NULL, StartUtc datetime2(0) NOT NULL,
                [Open] decimal(19,6) NOT NULL, High decimal(19,6) NOT NULL, Low decimal(19,6) NOT NULL, [Close] decimal(19,6) NOT NULL,
                Volume decimal(19,4) NOT NULL, TradeCount int NULL, Vwap decimal(19,6) NULL, Feed varchar(24) NOT NULL);
            CREATE CLUSTERED COLUMNSTORE INDEX CCI_MinuteBars ON dbo.MinuteBars ORDER (Symbol, StartUtc);
            CREATE TABLE dbo.DailyBars (
                Symbol varchar(16) NOT NULL, DateEt date NOT NULL,
                [Open] decimal(19,6) NOT NULL, High decimal(19,6) NOT NULL, Low decimal(19,6) NOT NULL, [Close] decimal(19,6) NOT NULL,
                Volume decimal(19,4) NOT NULL, Feed varchar(24) NOT NULL,
                CONSTRAINT PK_DailyBars PRIMARY KEY CLUSTERED (Symbol, DateEt));
            CREATE TABLE dbo.Coverage (
                Symbol varchar(16) NOT NULL, Kind varchar(16) NOT NULL, DateEt date NOT NULL, Items int NOT NULL, FetchedUtc datetime2(0) NOT NULL,
                CONSTRAINT PK_Coverage PRIMARY KEY CLUSTERED (Symbol, Kind, DateEt));
            CREATE TABLE dbo.Splits (
                Symbol varchar(16) NOT NULL, ExDate date NOT NULL, OldRate decimal(19,6) NOT NULL, NewRate decimal(19,6) NOT NULL, Source varchar(32) NOT NULL,
                CONSTRAINT PK_Splits PRIMARY KEY CLUSTERED (Symbol, ExDate));
            """),
        (2, "news, filings, backfill jobs", """
            CREATE TABLE dbo.News (
                Id uniqueidentifier NOT NULL CONSTRAINT PK_News PRIMARY KEY NONCLUSTERED,
                Source varchar(32) NOT NULL, ExternalId varchar(128) NOT NULL, PublishedUtc datetime2(0) NOT NULL, ReceivedUtc datetime2(0) NULL,
                StampKind varchar(16) NULL, Headline nvarchar(1000) NOT NULL, Url nvarchar(1000) NULL, SourceTier int NULL,
                PreClass varchar(32) NULL, Direction int NULL, SourceName nvarchar(200) NULL, Snippet nvarchar(max) NULL);
            CREATE UNIQUE CLUSTERED INDEX UX_News_Source ON dbo.News (Source, ExternalId);
            CREATE INDEX IX_News_Published ON dbo.News (PublishedUtc);
            CREATE TABLE dbo.NewsSymbols (
                NewsId uniqueidentifier NOT NULL, Symbol varchar(16) NOT NULL,
                CONSTRAINT PK_NewsSymbols PRIMARY KEY CLUSTERED (Symbol, NewsId));
            CREATE TABLE dbo.Filings (
                AccessionNumber varchar(32) NOT NULL, Symbol varchar(16) NOT NULL, Cik varchar(16) NULL, FormType varchar(24) NOT NULL,
                FiledDate date NOT NULL, AcceptedUtc datetime2(0) NULL, ItemsJson nvarchar(400) NULL,
                CONSTRAINT PK_Filings PRIMARY KEY CLUSTERED (Symbol, FiledDate, AccessionNumber));
            CREATE TABLE dbo.BackfillJobs (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BackfillJobs PRIMARY KEY,
                Symbol varchar(16) NOT NULL, Reason nvarchar(200) NOT NULL, FromEt date NOT NULL, ToEt date NOT NULL,
                Status varchar(16) NOT NULL, RequestedUtc datetime2(0) NOT NULL, StartedUtc datetime2(0) NULL, FinishedUtc datetime2(0) NULL,
                Error nvarchar(2000) NULL);
            CREATE INDEX IX_BackfillJobs_Status ON dbo.BackfillJobs (Status, RequestedUtc);
            """),
        (3, "coverage ranges (daily bars, splits, news, filings fetched over a span)", """
            CREATE TABLE dbo.CoverageRanges (
                Symbol varchar(16) NOT NULL, Kind varchar(16) NOT NULL, FromEt date NOT NULL, ToEt date NOT NULL, Items int NOT NULL, FetchedUtc datetime2(0) NOT NULL,
                Source nvarchar(100) NULL,
                CONSTRAINT PK_CoverageRanges PRIMARY KEY CLUSTERED (Symbol, Kind, FromEt, ToEt));
            """),
        (4, "filings primary document (read-through serves a fetchable document name)", """
            ALTER TABLE dbo.Filings ADD PrimaryDocument nvarchar(260) NULL;
            """),
        (5, "catalyst type on news and filings, non-binding LOI flag on news (IP-T487)", """
            ALTER TABLE dbo.News ADD CatalystType varchar(32) NULL, NonBindingLoi bit NULL;
            ALTER TABLE dbo.Filings ADD CatalystType varchar(32) NULL;
            """),
    ];

    /// <summary>
    /// Create the database if missing (a local server only) and apply every schema step not yet applied. Returns
    /// the steps applied now. On Azure SQL Database a missing database throws
    /// <see cref="IdiotProof.Core.Hosting.DatabaseNotProvisionedException"/> telling the operator to provision it.
    /// </summary>
    public async Task<IReadOnlyList<string>> EnsureSchemaAsync(CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("The history connection string names no database.");
        if (!await IdiotProof.Core.Hosting.SqlConnect.DatabaseExistsAsync(connectionString, "history", ct).ConfigureAwait(false))
        {
            builder.InitialCatalog = "master";
            await using var master = new SqlConnection(builder.ConnectionString);
            await master.OpenAsync(ct).ConfigureAwait(false);
            await using var create = new SqlCommand($"IF DB_ID(@n) IS NULL EXEC('CREATE DATABASE [' + @n + ']')", master);
            create.Parameters.AddWithValue("@n", name);
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await using (var v = new SqlCommand("IF OBJECT_ID('dbo.SchemaVersion') IS NULL CREATE TABLE dbo.SchemaVersion (Version int NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, AppliedUtc datetime2(0) NOT NULL)", c))
            await v.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var applied = new HashSet<int>();
        await using (var q = new SqlCommand("SELECT Version FROM dbo.SchemaVersion", c))
        await using (var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false)) applied.Add(r.GetInt32(0));

        var now = new List<string>();
        foreach (var (version, stepName, sql) in Steps.Where(st => !applied.Contains(st.Version)).OrderBy(st => st.Version))
        {
            await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (var cmd = new SqlCommand(sql, c, tx)) await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await using (var mark = new SqlCommand("INSERT dbo.SchemaVersion (Version, Name, AppliedUtc) VALUES (@v, @n, SYSUTCDATETIME())", c, tx))
            {
                mark.Parameters.AddWithValue("@v", version);
                mark.Parameters.AddWithValue("@n", stepName);
                await mark.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
            now.Add($"{version}: {stepName}");
        }
        return now;
    }
}
