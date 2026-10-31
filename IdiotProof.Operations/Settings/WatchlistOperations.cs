using System.Text.RegularExpressions;
using IdiotProof.Engine.Workspace;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Settings;

/// <summary>
/// IdiotProof's own watchlist: the symbols on the owner's workspace tabs (the <c>Workspaces</c>
/// table). The research scanner sweeps these every pass and scores news on them higher. Not
/// Alpaca's server-side watchlists (that is <c>idiotproof watchlists</c>).
/// </summary>
internal static partial class LocalWatchlist
{
    public static IReadOnlyList<WorkspaceTab> Tabs(OperationContext ctx) =>
        ctx.Get<IWorkspaceStore>().Load(ctx.RequireOwner().ToString()).OrderBy(t => t.DisplayOrder).ThenBy(t => t.CreatedUtc).ToList();

    /// <summary>The tab a change applies to: the named one, else the only one, else a new "Main" tab when there are none.</summary>
    public static WorkspaceTab Target(OperationContext ctx, string? workspace)
    {
        var tabs = Tabs(ctx);
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            var w = workspace.Trim();
            return tabs.FirstOrDefault(t => string.Equals(t.TabId, w, StringComparison.OrdinalIgnoreCase))
                ?? tabs.FirstOrDefault(t => string.Equals(t.Name, w, StringComparison.OrdinalIgnoreCase))
                ?? throw OperationException.Usage($"No workspace named '{w}'. Yours: {Names(tabs)}.");
        }
        return tabs.Count switch
        {
            0 => new WorkspaceTab { Name = "Main" },
            1 => tabs[0],
            _ => throw OperationException.Usage($"You have {tabs.Count} workspaces; pick one with --workspace: {Names(tabs)}."),
        };
    }

    public static List<string> Symbols(IEnumerable<string> raw)
    {
        var list = new List<string>();
        foreach (var s in raw.SelectMany(r => r.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)))
        {
            var sym = s.Trim().ToUpperInvariant();
            if (!Ticker().IsMatch(sym)) throw OperationException.Usage($"'{s}' is not a ticker symbol.");
            if (!list.Contains(sym)) list.Add(sym);
        }
        if (list.Count == 0) throw OperationException.Usage("Name at least one symbol.");
        return list;
    }

    private static string Names(IEnumerable<WorkspaceTab> tabs) => string.Join(", ", tabs.Select(t => $"'{t.Name}' ({t.TabId})"));

    [GeneratedRegex(@"^[A-Z][A-Z0-9.\-]{0,9}$")]
    private static partial Regex Ticker();
}

public sealed record WatchlistListArgs
{
    [Option("workspace", Description = "Only this workspace (name or id).")]
    public string? Workspace { get; init; }
}

/// <summary>Your watchlist symbols, per workspace (IP-T252).</summary>
[Operation("watchlist list", Summary = "Your IdiotProof watchlist: the symbols on each of your workspaces (not Alpaca's watchlists).", Risk = OperationRisk.Read)]
[Example("idiotproof watchlist list")]
[Example("idiotproof watchlist list --workspace Main")]
public sealed class WatchlistListOperation : IOperation<WatchlistListArgs>
{
    public Task<OperationResult> ExecuteAsync(WatchlistListArgs args, OperationContext ctx)
    {
        var tabs = string.IsNullOrWhiteSpace(args.Workspace) ? LocalWatchlist.Tabs(ctx) : [LocalWatchlist.Target(ctx, args.Workspace)];
        var data = tabs.Select(t => new { workspace = t.Name, id = t.TabId, symbols = t.Watchlist }).ToList();
        if (tabs.Count == 0 || tabs.All(t => t.Watchlist.Count == 0))
            return Task.FromResult(OperationResult.Ok(data, "Your watchlist is empty. Add symbols with `idiotproof watchlist add NVDA AAPL`."));

        var table = new TextTable { MaxWidth = ctx.Width }.Column("workspace").Column("id").Column("n", Align.Right).Column("symbols", maxWidth: 80);
        foreach (var t in tabs) table.Row(t.Name, t.TabId, t.Watchlist.Count.ToString(), string.Join(" ", t.Watchlist));
        return Task.FromResult(OperationResult.Ok(data, table.Render()));
    }
}

public sealed record WatchlistChangeArgs
{
    [Argument(0, Name = "symbols", Description = "One or more ticker symbols (space or comma separated).")]
    public string[] Symbols { get; init; } = [];

    [Option("workspace", Description = "Which workspace (name or id); needed only when you have more than one.")]
    public string? Workspace { get; init; }
}

/// <summary>Adds symbols to a workspace watchlist (IP-T252); already-present symbols are skipped.</summary>
[Operation("watchlist add", Summary = "Add symbols to your IdiotProof watchlist (creates a 'Main' workspace when you have none).", Risk = OperationRisk.Write)]
[Example("idiotproof watchlist add NVDA AAPL")]
[Example("idiotproof watchlist add TSLA --workspace Main")]
public sealed class WatchlistAddOperation : IOperation<WatchlistChangeArgs>
{
    public Task<OperationResult> ExecuteAsync(WatchlistChangeArgs args, OperationContext ctx)
    {
        var symbols = LocalWatchlist.Symbols(args.Symbols);
        var tab = LocalWatchlist.Target(ctx, args.Workspace);
        var added = symbols.Where(s => !tab.Watchlist.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        tab.Watchlist.AddRange(added);
        if (added.Count > 0) ctx.Get<IWorkspaceStore>().Save(ctx.RequireOwner().ToString(), tab);
        var skipped = symbols.Except(added).ToList();
        return Task.FromResult(OperationResult.Ok(
            new { workspace = tab.Name, id = tab.TabId, added, skipped, symbols = tab.Watchlist },
            (added.Count > 0 ? $"Added {string.Join(" ", added)} to '{tab.Name}'." : $"Nothing new for '{tab.Name}'.")
            + (skipped.Count > 0 ? $" Already there: {string.Join(" ", skipped)}." : "")
            + $" Watchlist now: {string.Join(" ", tab.Watchlist)}"));
    }
}

/// <summary>Removes symbols from a workspace watchlist (IP-T252).</summary>
[Operation("watchlist remove", Summary = "Remove symbols from your IdiotProof watchlist.", Risk = OperationRisk.Write)]
[Example("idiotproof watchlist remove AAPL")]
public sealed class WatchlistRemoveOperation : IOperation<WatchlistChangeArgs>
{
    public Task<OperationResult> ExecuteAsync(WatchlistChangeArgs args, OperationContext ctx)
    {
        var symbols = LocalWatchlist.Symbols(args.Symbols);
        var tab = LocalWatchlist.Target(ctx, args.Workspace);
        var removed = symbols.Where(s => tab.Watchlist.RemoveAll(w => string.Equals(w, s, StringComparison.OrdinalIgnoreCase)) > 0).ToList();
        if (removed.Count > 0) ctx.Get<IWorkspaceStore>().Save(ctx.RequireOwner().ToString(), tab);
        var missing = symbols.Except(removed).ToList();
        return Task.FromResult(OperationResult.Ok(
            new { workspace = tab.Name, id = tab.TabId, removed, missing, symbols = tab.Watchlist },
            (removed.Count > 0 ? $"Removed {string.Join(" ", removed)} from '{tab.Name}'." : $"Nothing removed from '{tab.Name}'.")
            + (missing.Count > 0 ? $" Not on it: {string.Join(" ", missing)}." : "")
            + $" Watchlist now: {(tab.Watchlist.Count == 0 ? "(empty)" : string.Join(" ", tab.Watchlist))}"));
    }
}
