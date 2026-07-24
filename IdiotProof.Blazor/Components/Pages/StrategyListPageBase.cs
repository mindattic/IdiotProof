using IdiotProof.Blazor.Data;
using IdiotProof.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;

namespace IdiotProof.Blazor.Components.Pages;

/// <summary>
/// Shared state/behavior for the Strategies (cards) and Dashboard (table) pages —
/// loading, live-mode elevation, broker-mode toggling, bulk actions, and delete.
/// Both pages need the full set (not just markup), so this is a base class rather
/// than duplicated code.
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

    protected async Task ToggleActive(Strategy s, bool isActive)
    {
        if (userId is null) return;
        var isGapper = s.ScriptText.Contains("PeakGiveback(", StringComparison.OrdinalIgnoreCase);
        if (isActive && !s.IsActive && isGapper
            && await StrategyRepo.CountActiveForSymbolAsync(userId.Value, s.Symbol) > 0)
        {
            loadError = $"{s.Symbol} already has an active gapper — pause it first (one active gapper per symbol).";
            return;
        }

        var result = await StrategyRepo.SetActiveAsync(s.Id, isActive, userId.Value);
        if (result == StrategyMutation.Ok)
        {
            s.IsActive = isActive;
            loadError = null;
            if (isActive && isGapper
                && await StrategyRepo.CountActiveForSymbolAsync(userId.Value, s.Symbol) > 1)
            {
                await StrategyRepo.SetActiveAsync(s.Id, false, userId.Value);
                s.IsActive = false;
                loadError = $"{s.Symbol} lost the race to another activation — left paused.";
            }
        }
        else if (result == StrategyMutation.PositionOpen)
        {
            loadError = $"{s.Symbol} is holding {s.PositionQty} shares — the Monitor must stay active to manage the exit. Flatten first.";
        }
    }

    protected async Task ApplyBulkActiveAsync(bool isActive)
    {
        if (userId is null || selectedIds.Count == 0) return;
        var ids = selectedIds.ToList();
        var (updated, skipped) = await StrategyRepo.SetActiveBulkAsync(ids, isActive, userId.Value);
        all = await StrategyRepo.GetAllForUserAsync(userId.Value);
        await RefreshProgressAsync();
        bulkMessage = skipped > 0
            ? $"⚠ {updated} {(isActive ? "activated" : "deactivated")}, {skipped} skipped (open positions)"
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
                pendingLiveStrategy = s;
                pendingBulkLive = false;
                OpenPasswordModal();
            }
        }
    }

    protected async Task SetSingleBrokerModeAsync(Strategy s, string mode)
    {
        if (userId is null) return;
        await StrategyRepo.SetBrokerModeAsync([s.Id], mode, userId.Value);
        s.BrokerMode = mode;
    }

    protected async Task ApplyBulkBrokerModeAsync(string mode)
    {
        if (userId is null || selectedIds.Count == 0) return;
        if (mode == "Live")
        {
            if (!ElevationSvc.IsElevated(userId.Value))
            {
                pendingBulkLive = true;
                pendingLiveStrategy = null;
                OpenPasswordModal();
                return;
            }
        }

        var ids = selectedIds.ToList();
        var count = await StrategyRepo.SetBrokerModeAsync(ids, mode, userId.Value);
        foreach (var s in all.Where(x => ids.Contains(x.Id))) s.BrokerMode = mode;
        bulkMessage = $"{count} set to {mode}";
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
        pendingLiveStrategy = null;
        pendingBulkLive = false;
        passwordInput = "";
        passwordModalError = "";
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
                foreach (var s in all.Where(x => ids.Contains(x.Id))) s.BrokerMode = "Live";
                bulkMessage = $"{count} set to Live";
                selectedIds.Clear();
                pendingBulkLive = false;
            }
            else if (pendingLiveStrategy is not null)
            {
                await SetSingleBrokerModeAsync(pendingLiveStrategy, "Live");
                pendingLiveStrategy = null;
            }
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
