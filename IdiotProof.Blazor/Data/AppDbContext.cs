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
    }
}
