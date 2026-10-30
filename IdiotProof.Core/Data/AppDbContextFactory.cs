using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> tooling. Runtime DbContext is
/// built via DI in Program.cs; this factory only fires for migration commands
/// (add/update/script). Connection-string resolution mirrors runtime exactly:
/// env var → fallback to LocalDB. Same pattern as Prose's
/// <c>ProseDbContextFactory</c>.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connStr =
            Environment.GetEnvironmentVariable("ConnectionStrings__IdiotProof")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=IdiotProof;Trusted_Connection=True;TrustServerCertificate=True;";

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connStr)
            .Options;

        return new AppDbContext(opts);
    }
}
