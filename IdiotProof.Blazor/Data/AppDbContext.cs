using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Blazor.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<UserApiKeys> UserApiKeys => Set<UserApiKeys>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<LearningArticle> LearningArticles => Set<LearningArticle>();
    public DbSet<SettingsKv>      SettingsKv       => Set<SettingsKv>();
    public DbSet<Workspace>       Workspaces       => Set<Workspace>();
    public DbSet<AuditLog>        AuditLogs        => Set<AuditLog>();
    public DbSet<ConditionProgress> ConditionProgress => Set<ConditionProgress>();

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
        });

        builder.Entity<LearningArticle>(e =>
        {
            e.HasIndex(a => a.Category);
            e.HasIndex(a => new { a.Category, a.Order });
        });

        builder.Entity<Workspace>(e =>
        {
            e.HasIndex(w => w.OwnerUserId);
            e.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(w => w.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
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
