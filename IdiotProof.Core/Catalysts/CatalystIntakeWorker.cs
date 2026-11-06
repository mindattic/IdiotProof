using System.Text.Json;
using IdiotProof.Core.Monitoring;
using IdiotProof.Core.Services;
using IdiotProof.DataFeeds.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdiotProof.Core.Catalysts;

/// <summary>
/// Hub-hosted catalyst intake (IP-T443), in the shape of <see cref="ExhaustionScanWorker"/>: idle
/// unless SettingsKv <c>catalyst.intake.enabled</c> is "true" and this instance holds the Monitor
/// leader lease. When on, it keeps ONE Alpaca news websocket open (subscribed to every symbol) with
/// the owner's data keys and hands each headline to <see cref="CatalystIngestor"/> stamped with the
/// instant it arrived; with <c>catalyst.intake.edgarPolling</c> it also polls EDGAR's latest filings
/// every minute. Writes research rows only; it never arms or trades (IP-LAW-10: the Monitor reads
/// the rows per tick).
/// </summary>
public sealed class CatalystIntakeWorker(
    CatalystIntakeSettings settings,
    CatalystIntakeState state,
    MonitorRuntimeState monitor,
    UserKeyService userKeys,
    IStreamSocketFactory sockets,
    IServiceScopeFactory scopes,
    EdgarLatestFilingsPoller edgar,
    AuditLogRepository auditLog,
    TimeProvider time,
    ILogger<CatalystIntakeWorker> log) : BackgroundService
{
    public const string NewsPath = "v1beta1/news";
    public const string AuditStart = "catalyst-intake-start";
    public const string AuditStop = "catalyst-intake-stop";
    public const string AuditError = "catalyst-intake-error";

    public static readonly TimeSpan Cadence = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan EdgarCadence = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RefusalCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorAuditSpacing = TimeSpan.FromMinutes(5);

    private CancellationTokenSource? newsCts;
    private Task? newsTask;
    private CancellationTokenSource? edgarCts;
    private Task? edgarTask;
    private DateTime blockedUntilUtc;
    private readonly Dictionary<string, DateTime> lastErrorAudit = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Cadence, time);
        try
        {
            do
            {
                try { await ReconcileAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { log.LogWarning(ex, "Catalyst intake reconcile failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            await StopNewsAsync("host stopping");
            await StopEdgarAsync();
        }
    }

    /// <summary>Starts or stops the stream and the EDGAR poller to match the settings and the lease; true when the stream is running afterwards.</summary>
    public async Task<bool> ReconcileAsync(CancellationToken ct)
    {
        var config = await settings.ReadAsync(ct);
        var leader = config.Enabled && await monitor.IsLeaderAsync(ct);
        state.SetConfig(config, leader);
        var wantNews = config.Enabled && leader && config.OwnerUserId is not null && Now >= blockedUntilUtc;
        var wantEdgar = wantNews && config.EdgarPolling;

        if (wantNews && (newsTask is null || newsTask.IsCompleted))
        {
            var keys = await ResolveKeysAsync(config.OwnerUserId!.Value, ct);
            if (keys is null)
            {
                blockedUntilUtc = Now + RefusalCooldown;
                await ErrorAsync("no-keys", $"owner {config.OwnerUserId} has no Alpaca data keys; retrying in {RefusalCooldown.TotalMinutes:0} min", config.OwnerUserId, ct);
                return false;
            }
            await StartNewsAsync(keys, config.OwnerUserId.Value, ct);
        }
        else if (!wantNews && newsTask is not null)
        {
            await StopNewsAsync(!config.Enabled ? "disabled" : !leader ? "not the leader" : config.OwnerUserId is null ? "owner unset" : "cooling down after a refusal");
        }

        if (wantEdgar && (edgarTask is null || edgarTask.IsCompleted)) StartEdgar();
        else if (!wantEdgar && edgarTask is not null) await StopEdgarAsync();

        return newsTask is { IsCompleted: false };
    }

    private async Task<AlpacaStreamKeys?> ResolveKeysAsync(Guid owner, CancellationToken ct)
    {
        var k = await userKeys.GetOrCreateAsync(owner, ct);
        return string.IsNullOrWhiteSpace(k.AlpacaApiKeyId) || string.IsNullOrWhiteSpace(k.AlpacaApiSecretKey)
            ? null
            : new AlpacaStreamKeys(k.AlpacaApiKeyId, k.AlpacaApiSecretKey);
    }

    private async Task StartNewsAsync(AlpacaStreamKeys keys, Guid owner, CancellationToken ct)
    {
        var session = new AlpacaStreamSession("catalyst intake · news", new Uri(AlpacaStreamSession.DataStreamBase + NewsPath), keys,
            () => new Dictionary<string, object?> { ["action"] = "subscribe", ["news"] = new[] { "*" } }, sockets, time);
        newsCts = new CancellationTokenSource();
        var token = newsCts.Token;
        state.MarkStarted(session, Now);
        await auditLog.LogAsync(AuditStart, "Catalyst intake started: Alpaca news stream (all symbols).", userId: owner, ct: ct);
        log.LogInformation("Catalyst intake: news stream starting for owner {Owner}.", owner);
        newsTask = Task.Run(() => RunNewsAsync(session, owner, token), CancellationToken.None);
    }

    private async Task RunNewsAsync(AlpacaStreamSession session, Guid owner, CancellationToken ct)
    {
        try
        {
            await session.RunAsync(m => OnMessageAsync(m, owner, ct), ct);
        }
        catch (AlpacaStreamRefusedException ex)
        {
            blockedUntilUtc = Now + RefusalCooldown;
            await ErrorAsync("refused", $"{ex.Message}; retrying in {RefusalCooldown.TotalMinutes:0} min", owner, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await ErrorAsync("stream", ex.Message, owner, CancellationToken.None);
        }
    }

    private async ValueTask OnMessageAsync(JsonElement message, Guid owner, CancellationToken ct)
    {
        if (!CatalystNewsMessage.IsNews(message)) return;
        var received = Now;
        var intake = CatalystNewsMessage.Parse(message, received);
        if (intake is null) return;
        try
        {
            using var scope = scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<CatalystIngestor>().IngestAsync(intake, ct);
            state.CountNews(result.Outcome, received);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await ErrorAsync("ingest", $"{intake.Source}/{intake.ExternalId}: {ex.Message}", owner, ct);
        }
    }

    private async Task StopNewsAsync(string why)
    {
        if (newsTask is null) return;
        newsCts?.Cancel();
        try { await newsTask; } catch { /* the run loop reports its own errors */ }
        newsCts?.Dispose();
        newsCts = null;
        newsTask = null;
        state.MarkStopped(Now);
        log.LogInformation("Catalyst intake: news stream stopped ({Why}).", why);
        try { await auditLog.LogAsync(AuditStop, $"Catalyst intake stopped: {why}.", ct: CancellationToken.None); }
        catch (Exception ex) { log.LogDebug(ex, "stop audit failed"); }
    }

    private void StartEdgar()
    {
        edgarCts = new CancellationTokenSource();
        var token = edgarCts.Token;
        edgarTask = Task.Run(() => RunEdgarAsync(token), CancellationToken.None);
    }

    private async Task RunEdgarAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(EdgarCadence, time);
        do
        {
            try
            {
                var summary = await edgar.PollOnceAsync(ct);
                state.RecordEdgarPoll(summary, Now);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                await ErrorAsync("edgar", ex.Message, null, CancellationToken.None);
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task StopEdgarAsync()
    {
        if (edgarTask is null) return;
        edgarCts?.Cancel();
        try { await edgarTask; } catch { /* reported inside the loop */ }
        edgarCts?.Dispose();
        edgarCts = null;
        edgarTask = null;
    }

    private async Task ErrorAsync(string kind, string message, Guid? owner, CancellationToken ct)
    {
        state.RecordError($"{kind}: {message}", Now);
        log.LogWarning("Catalyst intake {Kind}: {Message}", kind, message);
        lock (lastErrorAudit)
        {
            if (lastErrorAudit.TryGetValue(kind, out var last) && Now - last < ErrorAuditSpacing) return;
            lastErrorAudit[kind] = Now;
        }
        try { await auditLog.LogAsync(AuditError, $"Catalyst intake {kind}: {message}", userId: owner, ct: ct); }
        catch (Exception ex) { log.LogDebug(ex, "error audit failed"); }
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;
}
