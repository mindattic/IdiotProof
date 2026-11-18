using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdiotProof.Core.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> tooling. Runtime DbContext is
/// built via DI in Program.cs; this factory only fires for migration commands
/// (add/update/script). Connection-string resolution mirrors runtime exactly:
/// env var → fallback to the shared Azure SQL database
/// (<see cref="IdiotProof.Core.Hosting.CloudDatabases.Main"/>, IP-T545). There is no local fallback,
/// so with <c>ConnectionStrings__IdiotProof</c> unset <c>dotnet ef database update</c> now targets
/// the shared cloud database the Hub trades on: point the variable at a scratch database first when
/// trying a migration out.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connStr =
            Environment.GetEnvironmentVariable("ConnectionStrings__IdiotProof")
            ?? IdiotProof.Core.Hosting.CoreServiceRegistration.DefaultConnectionString;

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connStr)
            .Options;

        return new AppDbContext(opts);
    }
}
