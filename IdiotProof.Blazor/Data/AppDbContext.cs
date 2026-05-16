using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<UserApiKeys> UserApiKeys => Set<UserApiKeys>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<SettingsKv>      SettingsKv       => Set<SettingsKv>();
    public DbSet<AuditLog>        AuditLogs        => Set<AuditLog>();
    public DbSet<ConditionProgress> ConditionProgress => Set<ConditionProgress>();
    // LearningArticle + Workspace DbSets removed with the UI cruft sweep. The
    // tables still exist in the database from prior migrations; rebuild them
    // via a fresh migration if the concepts ever return.

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserApiKeys>(e =>
        {
            e.HasIndex(k => k.UserId).IsUnique();
        });

        builder.Entity<Strategy>(e =>
        {
            e.HasIndex(s => new { s.OwnerUserId, s.IsActive });
            e.HasIndex(s => s.Symbol);
            e.HasIndex(s => s.WorkspaceId);
            // Cascade-delete a user's strategies when the user is removed.
            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(s => s.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserPreferences>(e =>
        {
            // UserId is both PK and FK to AspNetUsers — one row per user.
            e.HasOne<AppUser>()
                .WithOne()
                .HasForeignKey<UserPreferences>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Risk Guardian decimals. Declared explicitly so EF stops warning
            // about silent default truncation. Matches the existing SQL column
            // type (decimal(18,2)) so this produces no migration delta.
            e.Property(p => p.RiskMaxLossPerTrade).HasPrecision(18, 2);
            e.Property(p => p.RiskMaxLossPerDay).HasPrecision(18, 2);
            e.Property(p => p.RiskAccountBalance).HasPrecision(18, 2);
            e.Property(p => p.RiskMinStopLossPercent).HasPrecision(18, 2);
            e.Property(p => p.RiskMaxStopLossPercent).HasPrecision(18, 2);
            e.Property(p => p.RiskMaxAccountRiskPercent).HasPrecision(18, 2);
        });

        builder.Entity<AuditLog>(e =>
        {
            // (UserId, Timestamp) supports per-user audit views; (Timestamp) supports
            // the global recent-events view. Descending sort handled by query, not index.
            e.HasIndex(a => new { a.UserId, a.TimestampUtc });
            e.HasIndex(a => a.TimestampUtc);
            e.HasIndex(a => a.Category);
        });

        builder.Entity<ConditionProgress>(e =>
        {
            // Index EvaluatedUtc for "stale row" cleanup queries.
            e.HasIndex(p => p.EvaluatedUtc);
            e.HasOne<Strategy>()
                .WithOne()
                .HasForeignKey<ConditionProgress>(p => p.StrategyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
