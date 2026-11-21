using IdiotProof.Core.Catalysts;
using IdiotProof.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Core.Tests;

/// <summary>IP-T627: the formation universe's catalyst query must translate to SQL; InMemory evaluates anything, so this builds the SQL Server command text without connecting.</summary>
[TestFixture]
public sealed class CatalystLookupTranslationTests
{
    [Test]
    public void RecentSymbolsQuery_TranslatesToSqlServer_WithOrderAndTopBeforeTheProjection()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=translation-only;Integrated Security=true;TrustServerCertificate=true")
            .Options);

        var sql = CatalystLookup.RecentSymbolsQuery(db, new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc), 5000).ToQueryString();

        Assert.That(sql, Does.Contain("TOP"));
        Assert.That(sql, Does.Contain("ORDER BY"));
        Assert.That(sql, Does.Contain("PublishedUtc"));
    }
}
