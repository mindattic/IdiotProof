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
    public DbSet<TradeDiaryEntry>   TradeDiary        => Set<TradeDiaryEntry>();
    public DbSet<BlockedEmailDomain> DomainNameBlacklist => Set<BlockedEmailDomain>();
    public DbSet<ReplayRun>         ReplayRuns        => Set<ReplayRun>();
    public DbSet<ReplayTrade>       ReplayTrades      => Set<ReplayTrade>();
    public DbSet<ReplayBar>         ReplayBars        => Set<ReplayBar>();
    public DbSet<AutoGapperScan>       AutoGapperScans      => Set<AutoGapperScan>();
    public DbSet<AutoGapperCandidate>  AutoGapperCandidates => Set<AutoGapperCandidate>();

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

        b.Entity<ReplayRun>(e =>
        {
            // Read patterns: per-ticker history (newest first) and the root
            // all-tickers index. No FK — a replay is a permanent archived
            // artifact that must survive its strategy/profile being changed.
            e.HasIndex(r => new { r.Symbol, r.GeneratedUtc });
            e.HasIndex(r => r.GeneratedUtc);
        });

        // Normalized ML feature store: trades/bars cascade with their run.
        b.Entity<ReplayTrade>(e =>
        {
            e.HasIndex(t => t.ReplayRunId);
            e.HasIndex(t => new { t.Symbol, t.Won });
            e.HasOne<ReplayRun>().WithMany().HasForeignKey(t => t.ReplayRunId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReplayBar>(e =>
        {
            e.HasIndex(x => x.ReplayRunId);
            e.HasOne<ReplayRun>().WithMany().HasForeignKey(x => x.ReplayRunId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AutoGapperScan>(e =>
        {
            // Idempotency is per (date, phase): the scheduled "auto" pass runs at
            // most once per ET day, while "manual" pre-arms may repeat — so the
            // index is NOT unique (the once-per-day guard is a code-side query).
            e.HasIndex(s => new { s.ScanEtDate, s.Phase });
        });
        b.Entity<AutoGapperCandidate>(e =>
        {
            e.HasIndex(c => c.ScanId);
            e.HasIndex(c => new { c.Symbol, c.ScanEtDate });
            e.Property(c => c.Notional).HasPrecision(18, 2);
            e.HasOne<AutoGapperScan>().WithMany().HasForeignKey(c => c.ScanId).OnDelete(DeleteBehavior.Cascade);
            // NO FK to Strategy: the candidate is a permanent research record and
            // must survive the armed strategy being deleted (like TradeDiary).
        });

        b.Entity<TradeDiaryEntry>(e =>
        {
            // Read patterns: per-user recent-first, per-strategy history, and
            // "still open" lookups when the Monitor closes a trade on exit.
            e.HasIndex(t => new { t.OwnerUserId, t.EntryUtc });
            e.HasIndex(t => new { t.StrategyId, t.Status });
            // NO FK to Strategy or AuthUser: the diary is a permanent record and
            // must survive a strategy/account being deleted (unlike
            // ConditionProgress, which is transient per-tick state that cascades).
        });
    }
}
