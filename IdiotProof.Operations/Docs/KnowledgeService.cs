using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IdiotProof.Operations.Docs;

/// <summary>
/// The Hub's one <see cref="KnowledgeIndex"/>: built in the background at Hub start
/// (<see cref="KnowledgeWarmup"/>), rebuilt on demand (<c>--refresh</c>) or once it is older than
/// <see cref="MaxAge"/>, so docs other agents commit during the day show up without a restart.
/// The <c>docs</c> operations read it through <see cref="From"/>.
/// </summary>
public sealed class KnowledgeService
{
    private readonly Func<CancellationToken, KnowledgeIndex?> build;
    private readonly SemaphoreSlim gate = new(1, 1);
    private KnowledgeIndex? current;

    public KnowledgeService(Func<CancellationToken, KnowledgeIndex?> build, ITicketCli? tickets = null)
    {
        this.build = build;
        Tickets = new TicketHistory(tickets ?? new TicketsProcess());
    }

    /// <summary>An index that is always <paramref name="index"/> (tests, fixed hosts).</summary>
    public static KnowledgeService Fixed(KnowledgeIndex index, ITicketCli? tickets = null) => new(_ => index, tickets) { MaxAge = TimeSpan.MaxValue };

    /// <summary>The host default: the checkout found from <see cref="KnowledgeIndexBuilder.RepoEnvVar"/>, the host folder or the working directory.</summary>
    public static KnowledgeService ForHost()
    {
        var tickets = new TicketsProcess();
        return new KnowledgeService(ct =>
            KnowledgeIndexBuilder.FindRepoRoot(AppContext.BaseDirectory, Environment.CurrentDirectory) is { } root
                ? new KnowledgeIndexBuilder(root) { Tickets = tickets }.Build(ct)
                : null, tickets);
    }

    /// <summary>The shared instance for hosts that did not register one.</summary>
    public static KnowledgeService Shared { get; } = ForHost();

    /// <summary>The registered service, else <see cref="Shared"/>.</summary>
    public static KnowledgeService From(OperationContext ctx) => ctx.Services.GetService<KnowledgeService>() ?? Shared;

    /// <summary>How old an index may get before the next call rebuilds it.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Ticket details on demand (<c>docs ticket</c>), through the same read-only CLI.</summary>
    public TicketHistory Tickets { get; }

    /// <summary>The index, building it first when there is none, it is stale, or <paramref name="refresh"/> is set.</summary>
    /// <exception cref="OperationException">Failed when no checkout could be found.</exception>
    public async Task<KnowledgeIndex> GetAsync(bool refresh = false, CancellationToken ct = default)
    {
        var snapshot = current;
        if (snapshot is not null && !refresh && !IsStale(snapshot)) return snapshot;

        await gate.WaitAsync(ct);
        try
        {
            if (current is not null && !refresh && !IsStale(current)) return current;
            var built = await Task.Run(() => build(ct), ct);
            current = built ?? throw OperationException.Failed(
                $"No IdiotProof checkout found to read docs from. Set {KnowledgeIndexBuilder.RepoEnvVar} to the repo folder and restart the Hub.");
            return current;
        }
        finally { gate.Release(); }
    }

    private bool IsStale(KnowledgeIndex index) => MaxAge != TimeSpan.MaxValue && DateTime.UtcNow - index.BuiltUtc > MaxAge;
}

/// <summary>Builds the knowledge index in the background when the Hub starts, so the first <c>docs</c> call is fast.</summary>
public sealed class KnowledgeWarmup(KnowledgeService knowledge) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await knowledge.GetAsync(ct: stoppingToken); }
        catch (Exception e) when (e is OperationException or IOException or OperationCanceledException) { /* the first docs call reports it */ }
    }
}
