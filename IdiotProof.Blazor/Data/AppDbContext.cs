using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Data;
using MindAttic.Authentication.Entities;

namespace IdiotProof.Blazor.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAuthDataContext
{
    // Auth tables (MindAttic.Authentication)
    public DbSet<AuthUser>               AuthUsers               => Set<AuthUser>();
    public DbSet<AuthUserMfa>            AuthUserMfa             => Set<AuthUserMfa>();
    public DbSet<AuthRecoveryCode>       AuthRecoveryCodes       => Set<AuthRecoveryCode>();
    public DbSet<AuthSession>            AuthSessions            => Set<AuthSession>();
    public DbSet<AuthLoginThrottle>      AuthLoginThrottles      => Set<AuthLoginThrottle>();
    public DbSet<AuthAuditLog>           AuthAuditLog            => Set<AuthAuditLog>();
    public DbSet<AuthPasswordHistory>    AuthPasswordHistory     => Set<AuthPasswordHistory>();
    public DbSet<AuthPasswordResetToken> AuthPasswordResetTokens => Set<AuthPasswordResetToken>();

    // App tables
    public DbSet<UserApiKeys>       UserApiKeys       => Set<UserApiKeys>();
    public DbSet<Strategy>          Strategies        => Set<Strategy>();
    public DbSet<UserPreferences>   UserPreferences   => Set<UserPreferences>();
    public DbSet<SettingsKv>        SettingsKv        => Set<SettingsKv>();
    public DbSet<AuditLog>          AuditLogs         => Set<AuditLog>();
    public DbSet<ConditionProgress> ConditionProgress => Set<ConditionProgress>();
    public DbSet<WorkspaceRow>      Workspaces        => Set<WorkspaceRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyMindAtticAuthConfiguration();

        b.Entity<UserApiKeys>(e =>
        {
            e.HasIndex(k => k.UserId).IsUnique();
        });

        b.Entity<Strategy>(e =>
        {
            e.HasIndex(s => new { s.OwnerUserId, s.IsActive });
            e.HasIndex(s => s.Symbol);
            e.HasIndex(s => s.WorkspaceId);
            e.HasOne<AuthUser>()
                .WithMany()
                .HasForeignKey(s => s.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserPreferences>(e =>
        {
            e.HasOne<AuthUser>()
                .WithOne()
                .HasForeignKey<UserPreferences>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.RiskMaxLossPerTrade).HasPrecision(18, 2);
            e.Property(p => p.RiskMaxLossPerDay).HasPrecision(18, 2);
            e.Property(p => p.RiskAccountBalance).HasPrecision(18, 2);
            e.Property(p => p.RiskMinStopLossPercent).HasPrecision(18, 2);
            e.Property(p => p.RiskMaxStopLossPercent).HasPrecision(18, 2);
            e.Property(p => p.RiskMaxAccountRiskPercent).HasPrecision(18, 2);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.TimestampUtc });
            e.HasIndex(a => a.TimestampUtc);
            e.HasIndex(a => a.Category);
        });

        b.Entity<ConditionProgress>(e =>
        {
            e.HasIndex(p => p.EvaluatedUtc);
            e.HasOne<Strategy>()
                .WithOne()
                .HasForeignKey<ConditionProgress>(p => p.StrategyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkspaceRow>(e =>
        {
            e.HasIndex(w => w.OwnerUserId);
            e.HasOne<AuthUser>()
                .WithMany()
                .HasForeignKey(w => w.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
