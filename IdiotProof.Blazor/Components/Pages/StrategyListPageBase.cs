using IdiotProof.Core.Data;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Components.Pages;

/// <summary>
/// Shared state/behavior for the Strategies page (table with expandable rows) —
/// loading, live-mode elevation, broker-mode toggling, bulk actions, and delete.
/// (Strategies and Dashboard were previously separate cards/table pages; merged
/// back into one table with expand-to-detail rows — this base class predates
/// the merge and still separates state from markup.)
/// </summary>
public abstract class StrategyListPageBase : ComponentBase, IDisposable
{
    [Inject] protected StrategyRepository StrategyRepo { get; set; } = null!;
    [Inject] protected ConditionProgressRepository ProgressRepo { get; set; } = null!;
    [Inject] protected ConditionProgressPusher ProgressPusher { get; set; } = null!;
    [Inject] protected LiveModeElevationService ElevationSvc { get; set; } = null!;
    [Inject] protected NavigationManager Nav { get; set; } = null!;
    [Inject] protected AuthenticationStateProvider AuthState { get; set; } = null!;

    protected bool loading = true;
    protected string? loadError;
    protected Guid? userId;
    protected string filterText = "";

    protected List<Strategy> all = new();
    protected readonly Dictionary<Guid, ConditionProgress> progress = new();
    protected Strategy? deleteCandidate;

    protected readonly HashSet<Guid> selectedIds = [];
    protected string bulkMessage = "";

    protected bool showPasswordModal;
    protected string passwordInput = "";
    protected string passwordModalError = "";
    protected bool isVerifyingPassword;
    protected Strategy? pendingLiveStrategy;
    protected bool pendingBulkLive;
    /// <summary>A Live strategy waiting on a fresh password before it is activated.</summary>
    protected Strategy? pendingLiveActivation;
    /// <summary>A bulk activation (containing Live rows) waiting on a fresh password.</summary>
    protected bool pendingBulkActivate;
    protected List<Guid>? pendingBulkActivateIds;
    /// <summary>What the password modal is confirming; null = the broker-mode elevation text.</summary>
    protected string? passwordModalNote;

    protected List<Strategy> filtered => string.IsNullOrWhiteSpace(filterText)
        ? all
        : all.Where(s =>
            s.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase)
         || s.Symbol.Contains(filterText, StringComparison.OrdinalIgnoreCase)
         || (s.Description?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var auth = await AuthState.GetAuthenticationStateAsync();
            var rawId = auth.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            userId = Guid.TryParse(rawId, out var uid) ? uid : null;
            if (userId is null) { loadError = "Sign in to view your strategies."; return; }

            all = await StrategyRepo.GetAllForUserAsync(userId.Value);
            await RefreshProgressAsync();
            ProgressPusher.ProgressChanged += OnProgressChanged;
        }
        catch (Exception ex) { loadError = ex.Message; }
        finally { loading = false; }
    }

    private async void OnProgressChanged(IReadOnlyList<Guid> changedIds)
    {
        try { await InvokeAsync(async () => { await RefreshProgressAsync(); StateHasChanged(); }); }
        catch { /* circuit tearing down */ }
    }

    protected async Task RefreshProgressAsync()
    {
        if (userId is not null)
        {
            var fresh = await StrategyRepo.GetAllForUserAsync(userId.Value);
            var byId = fresh.ToDictionary(s => s.Id);
            foreach (var s in all)
            {
                if (!byId.TryGetValue(s.Id, out var f)) continue;
                s.IsActive       = f.IsActive;
                s.BrokerMode     = f.BrokerMode;
                s.LastFiredUtc   = f.LastFiredUtc;
                s.FireCount      = f.FireCount;
                s.PositionQty    = f.PositionQty;
                s.LastEntryPrice = f.LastEntryPrice;
                s.EntryFilledUtc = f.EntryFilledUtc;
                s.LastExitPrice  = f.LastExitPrice;
                s.LastExitReason = f.LastExitReason;
                s.LastExitedUtc  = f.LastExitedUtc;
            }
        }

        var ids = all.Where(s => s.IsActive).Select(s => s.Id).ToList();
        if (ids.Count == 0) { progress.Clear(); return; }
        var rows = await ProgressRepo.GetForStrategyIdsAsync(ids);
        progress.Clear();
        foreach (var (k, v) in rows) progress[k] = v;
    }

    public void Dispose() => ProgressPusher.ProgressChanged -= OnProgressChanged;

    // ── Multi-select ──────────────────────────────────────────────────────────

    protected void ToggleSelect(Guid id) { if (!selectedIds.Add(id)) selectedIds.Remove(id); }

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        if ((bool)(e.Value ?? false))
            foreach (var s in filtered) selectedIds.Add(s.Id);
        else
            selectedIds.Clear();
    }

    // ── Active toggle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Activates/pauses one strategy. The repository enforces the gapper rules (one per symbol,
    /// the 3-gapper cap, post-write recheck — IP-LAW-11). Activating a strategy that routes to
    /// Live needs a fresh password for that activation — the web's equivalent of the CLI's
    /// per-action confirmation code. The Live check runs against the FRESH row in the repository,
    /// so a page that still shows Paper can't skip it.
    /// </summary>
    protected async Task ToggleActive(Strategy s, bool isActive, bool liveConfirmed = false)
    {
        if (userId is null) return;
        var result = await StrategyRepo.SetActiveAsync(s.Id, isActive, userId.Value, liveActivationConfirmed: liveConfirmed);
        switch (result)
        {
            case StrategyMutation.Ok:
                s.IsActive = isActive;
                loadError = null;
                break;
            case StrategyMutation.LiveNotConfirmed:
                s.BrokerMode = "Live";
                ClearPendingPasswordActions();
                pendingLiveActivation = s;
                passwordModalNote = $"You are activating {s.Symbol} '{s.Title}', which routes to LIVE — the Monitor will trade real money when its conditions match. Re-enter your password to confirm this activation.";
                OpenPasswordModal();
                break;
            case StrategyMutation.PositionOpen:
                loadError = $"{s.Symbol} is holding {s.PositionQty} shares — the Monitor must stay active to manage the exit. Flatten first.";
                break;
            case StrategyMutation.NotFound:
                break;
            default:
                loadError = StrategyRepository.Describe(result, s.Symbol);
                break;
        }
    }

    protected async Task ApplyBulkActiveAsync(bool isActive) => await ApplyBulkActiveAsync(isActive, liveConfirmed: false);

    private async Task ApplyBulkActiveAsync(bool isActive, bool liveConfirmed, List<Guid>? confirmedIds = null)
    {
        if (userId is null) return;
        // A confirmed batch is exactly the one the password was asked for, never a later selection.
        var ids = confirmedIds ?? selectedIds.ToList();
        if (ids.Count == 0) return;

        // A batch that would turn on any Live strategy needs one fresh password for the batch
        // (the CLI's `strategy bulk activate` needs one confirmation code). Checked against fresh
        // rows; the repository also skips Live rows when unconfirmed, closing the stale-read race.
        if (isActive && !liveConfirmed)
        {
            var fresh = await StrategyRepo.GetAllForUserAsync(userId.Value);
            var live = fresh.Where(x => ids.Contains(x.Id) && !x.IsActive && x.BrokerMode == "Live").ToList();
            if (live.Count > 0)
            {
                ClearPendingPasswordActions();
                pendingBulkActivate = true;
                pendingBulkActivateIds = ids;
                passwordModalNote = $"You are activating {ids.Count} strategies, {live.Count} of them LIVE ({string.Join(", ", live.Select(x => x.Symbol))}) — the Monitor will trade real money when their conditions match. Re-enter your password to confirm this activation.";
                OpenPasswordModal();
                return;
            }
        }

        var (updated, skippedPosition, skippedGapper, skippedLive) =
            await StrategyRepo.SetActiveBulkAsync(ids, isActive, userId.Value, liveActivationConfirmed: liveConfirmed);
        all = await StrategyRepo.GetAllForUserAsync(userId.Value);
        await RefreshProgressAsync();
        var notes = new List<string>();
        if (skippedPosition > 0) notes.Add($"{skippedPosition} skipped (open positions)");
        if (skippedGapper > 0) notes.Add($"{skippedGapper} skipped (one gapper per symbol / {StrategyRepository.MaxActiveGappers}-gapper cap)");
        if (skippedLive > 0) notes.Add($"{skippedLive} skipped (now Live — activate again to confirm)");
        bulkMessage = notes.Count > 0
            ? $"⚠ {updated} {(isActive ? "activated" : "deactivated")}, {string.Join(", ", notes)}"
            : $"{updated} {(isActive ? "activated" : "deactivated")}";
        selectedIds.Clear();
    }

    // ── Broker mode ───────────────────────────────────────────────────────────

    protected async Task ToggleBrokerModeAsync(Strategy s)
    {
        if (s.BrokerMode == "Live")
        {
            // Live → Paper is always immediate
            await SetSingleBrokerModeAsync(s, "Paper");
        }
        else
        {
            // Paper/Sandbox → Live requires elevation
            if (userId is not null && ElevationSvc.IsElevated(userId.Value))
                await SetSingleBrokerModeAsync(s, "Live");
            else
            {
                ClearPendingPasswordActions();
                pendingLiveStrategy = s;
                OpenPasswordModal();
            }
        }
    }

    protected async Task SetSingleBrokerModeAsync(Strategy s, string mode)
    {
        if (userId is null) return;
        if (await StrategyRepo.SetBrokerModeAsync([s.Id], mode, userId.Value) == 0)
        {
            loadError = s.PositionQty > 0
                ? $"{s.Symbol} is holding {s.PositionQty} shares in {s.BrokerMode}; changing the route would strand that position. Flatten first."
                : $"{s.Symbol}: the route was not changed (the strategy no longer exists, isn't yours, or now holds a position).";
            return;
        }
        s.BrokerMode = mode;
    }

    protected async Task ApplyBulkBrokerModeAsync(string mode)
    {
        if (userId is null || selectedIds.Count == 0) return;
        if (mode == "Live")
        {
            if (!ElevationSvc.IsElevated(userId.Value))
            {
                ClearPendingPasswordActions();
                pendingBulkLive = true;
                OpenPasswordModal();
                return;
            }
        }

        var ids = selectedIds.ToList();
        var count = await StrategyRepo.SetBrokerModeAsync(ids, mode, userId.Value);
        foreach (var s in all.Where(x => ids.Contains(x.Id) && x.PositionQty <= 0)) s.BrokerMode = mode;
        bulkMessage = count < ids.Count ? $"{count} set to {mode}, {ids.Count - count} skipped (open positions)" : $"{count} set to {mode}";
        selectedIds.Clear();
    }

    // ── Elevation / password modal ────────────────────────────────────────────

    protected void OpenPasswordModal()
    {
        passwordInput = "";
        passwordModalError = "";
        showPasswordModal = true;
    }

    protected void CancelPasswordModal()
    {
        showPasswordModal = false;
        ClearPendingPasswordActions();
        passwordInput = "";
        passwordModalError = "";
    }

    private void ClearPendingPasswordActions()
    {
        pendingLiveStrategy = null;
        pendingBulkLive = false;
        pendingLiveActivation = null;
        pendingBulkActivate = false;
        pendingBulkActivateIds = null;
        passwordModalNote = null;
    }

    protected void OnPasswordModalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") CancelPasswordModal();
    }

    protected async Task ConfirmPasswordModalAsync()
    {
        if (userId is null || string.IsNullOrEmpty(passwordInput)) return;
        isVerifyingPassword = true;
        passwordModalError = "";
        StateHasChanged();
        try
        {
            var ok = await ElevationSvc.VerifyPasswordAsync(userId.Value, passwordInput);
            if (!ok) { passwordModalError = "Incorrect password. Try again."; return; }

            ElevationSvc.Elevate(userId.Value);
            showPasswordModal = false;

            // Apply the pending action
            if (pendingBulkLive)
            {
                var ids = selectedIds.ToList();
                var count = await StrategyRepo.SetBrokerModeAsync(ids, "Live", userId.Value);
                foreach (var s in all.Where(x => ids.Contains(x.Id) && x.PositionQty <= 0)) s.BrokerMode = "Live";
                bulkMessage = count < ids.Count ? $"{count} set to Live, {ids.Count - count} skipped (open positions)" : $"{count} set to Live";
                selectedIds.Clear();
                pendingBulkLive = false;
            }
            else if (pendingLiveStrategy is not null)
            {
                await SetSingleBrokerModeAsync(pendingLiveStrategy, "Live");
                pendingLiveStrategy = null;
            }
            else if (pendingLiveActivation is not null)
            {
                // This password confirmed exactly this activation (one confirmation per action).
                var s = pendingLiveActivation;
                ClearPendingPasswordActions();
                await ToggleActive(s, true, liveConfirmed: true);
            }
            else if (pendingBulkActivate && pendingBulkActivateIds is { } batch)
            {
                ClearPendingPasswordActions();
                await ApplyBulkActiveAsync(true, liveConfirmed: true, confirmedIds: batch);
            }
            passwordModalNote = null;
        }
        finally
        {
            isVerifyingPassword = false;
            passwordInput = "";
        }
    }

    protected void RevokeElevation()
    {
        if (userId is not null) ElevationSvc.Revoke(userId.Value);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    protected void EditStrategy(Guid id) => Nav.NavigateTo($"/builder/{id}");
    protected void CreateNew() => Nav.NavigateTo("/builder");
    protected void ConfirmDelete(Strategy s) => deleteCandidate = s;

    protected async Task DeleteConfirmed()
    {
        if (deleteCandidate is null || userId is null) return;
        var result = await StrategyRepo.DeleteAsync(deleteCandidate.Id, userId.Value);
        if (result == StrategyMutation.PositionOpen)
        {
            loadError = $"{deleteCandidate.Symbol} is holding {deleteCandidate.PositionQty} shares — deleting would discard exit rules for a live position. Flatten first.";
            deleteCandidate = null;
            return;
        }
        all.RemoveAll(x => x.Id == deleteCandidate.Id);
        deleteCandidate = null;
        loadError = null;
    }

    // ── Badge helpers ─────────────────────────────────────────────────────────

    protected static string BrokerModeClass(string mode) => mode switch
    {
        "Live"    => "btn-danger",
        "Paper"   => "btn-warning",
        _         => "btn-secondary",
    };

    protected static string BrokerModeTitle(string mode) => mode switch
    {
        "Live"  => "LIVE trading with real money. Click to switch to Paper.",
        "Paper" => "Paper trading (simulated). Click to switch to Live (requires authentication).",
        _       => "Sandbox fallback — configure Alpaca keys to use Paper or Live.",
    };
}
