using System.Net;
using System.Text.Json;
using IdiotProof.Brokers;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Reference;

// Alpaca's server-side watchlists (/v2/watchlists), per account. Distinct from IdiotProof's own
// local watchlist (`watchlist ...`, IP-T252). Mutations need no confirmation code; the Hub ledgers them.

/// <summary>Resolving a watchlist by id or name, and rendering one.</summary>
internal static class AlpacaWatchlists
{
    /// <summary>The full watchlist (with assets) for an id or a name; usage error when Alpaca has none.</summary>
    public static async Task<JsonElement> ResolveAsync(AlpacaApi api, string idOrName, CancellationToken ct)
    {
        var key = idOrName.Trim();
        if (key.Length == 0) throw OperationException.Usage("Name a watchlist (its id or its name; see idiotproof watchlists list).");
        try
        {
            return Guid.TryParse(key, out var id)
                ? await api.GetAsync($"/v2/watchlists/{id}", null, ct)
                : await api.GetAsync("/v2/watchlists:by_name", new Dictionary<string, string?> { ["name"] = key }, ct);
        }
        catch (AlpacaApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            throw OperationException.Failed($"No Alpaca watchlist '{key}' on this account ({ex.AlpacaMessage}).");
        }
    }

    public static string Id(JsonElement w) => w.Str("id") ?? throw OperationException.Failed("Alpaca returned a watchlist without an id.");

    public static string[] Symbols(JsonElement w) => [.. w.Items("assets").Select(a => a.Str("symbol")).OfType<string>()];

    public static IEnumerable<string> Normalize(IEnumerable<string> symbols) =>
        ReferenceSupport.Csv(symbols).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToUpperInvariant()).Distinct();

    public static object Shape(JsonElement w, AlpacaAccount account) => new
    {
        account = OperationParameter.EnumWireName(account),
        id = w.Str("id"),
        name = w.Str("name"),
        createdAt = w.Str("created_at"),
        updatedAt = w.Str("updated_at"),
        symbols = Symbols(w),
    };

    public static string Render(JsonElement w, AlpacaAccount account, int? width)
    {
        var head = new KeyValueBlock()
            .Add("Watchlist", w.Str("name"))
            .Add("Account", account.ToString())
            .Add("Id", w.Str("id"))
            .Add("Updated", w.Instant("updated_at"))
            .Render();
        var assets = w.Items("assets").ToList();
        if (assets.Count == 0) return head + "\n(no symbols)";
        var table = new TextTable { MaxWidth = width }.Column("Symbol").Column("Name", maxWidth: 40).Column("Exchange").Column("Class").Column("Tradable");
        foreach (var a in assets) table.Row(a.Str("symbol"), a.Str("name"), a.Str("exchange"), a.Str("class"), ReferenceSupport.YesNo(a.Bool("tradable")));
        return $"{head}\n\n{table.Render()}";
    }
}

public sealed record WatchlistsListArgs
{
    [Option("mode", Description = "Alpaca account: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("watchlists list", Summary = "Your Alpaca server-side watchlists (not IdiotProof's local watchlist): name, id, last update.", Risk = OperationRisk.Read)]
[Example("idiotproof watchlists list")]
[Example("idiotproof watchlists list --mode live")]
public sealed class WatchlistsListOperation : IOperation<WatchlistsListArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistsListArgs args, OperationContext ctx)
    {
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var lists = (await api.GetAsync("/v2/watchlists", null, ctx.CancellationToken)).Items()
            .OrderBy(w => w.Str("name"), StringComparer.OrdinalIgnoreCase).ToList();
        if (lists.Count == 0) return OperationResult.Ok(Array.Empty<object>(), $"No Alpaca watchlists on the {args.Mode.ToString().ToLowerInvariant()} account.");
        var table = new TextTable { MaxWidth = ctx.Width }.Column("Name").Column("Id").Column("Created").Column("Updated");
        foreach (var w in lists) table.Row(w.Str("name"), w.Str("id"), w.Instant("created_at"), w.Instant("updated_at"));
        return OperationResult.Ok(lists.Select(w => AlpacaWatchlists.Shape(w, args.Mode)), table.Render());
    }
}

public sealed record WatchlistRefArgs
{
    [Argument(0, Name = "watchlist", Description = "Watchlist id or name.")]
    public string Watchlist { get; init; } = "";

    [Option("mode", Description = "Alpaca account: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("watchlists show", Summary = "One Alpaca watchlist (by id or name) and its symbols.", Risk = OperationRisk.Read)]
[Example("idiotproof watchlists show tech")]
[Example("idiotproof watchlists show 3174d6df-7726-44b4-a5bd-7fda5ae6e009 --mode live")]
public sealed class WatchlistsShowOperation : IOperation<WatchlistRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistRefArgs args, OperationContext ctx)
    {
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var w = await AlpacaWatchlists.ResolveAsync(api, args.Watchlist, ctx.CancellationToken);
        return OperationResult.Ok(AlpacaWatchlists.Shape(w, args.Mode), AlpacaWatchlists.Render(w, args.Mode, ctx.Width));
    }
}

public sealed record WatchlistsCreateArgs
{
    [Argument(0, Name = "name", Description = "Name for the new watchlist.")]
    public string Name { get; init; } = "";

    [Argument(1, Name = "symbols", Description = "Symbols to start with (space or comma separated).", Required = false)]
    public string[] Symbols { get; init; } = [];

    [Option("mode", Description = "Alpaca account: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("watchlists create", Summary = "Create an Alpaca watchlist, optionally with symbols.", Risk = OperationRisk.Write)]
[Example("idiotproof watchlists create tech AAPL MSFT NVDA")]
public sealed class WatchlistsCreateOperation : IOperation<WatchlistsCreateArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistsCreateArgs args, OperationContext ctx)
    {
        var name = args.Name.Trim();
        if (name.Length == 0) return OperationResult.Usage("Name the watchlist, e.g. idiotproof watchlists create tech AAPL MSFT.");
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var w = await api.SendAsync(HttpMethod.Post, "/v2/watchlists",
            new { name, symbols = AlpacaWatchlists.Normalize(args.Symbols).ToArray() }, null, ctx.CancellationToken);
        return OperationResult.Ok(AlpacaWatchlists.Shape(w, args.Mode), "Created.\n" + AlpacaWatchlists.Render(w, args.Mode, ctx.Width));
    }
}

public sealed record WatchlistsRenameArgs
{
    [Argument(0, Name = "watchlist", Description = "Watchlist id or current name.")]
    public string Watchlist { get; init; } = "";

    [Argument(1, Name = "new-name", Description = "The new name.")]
    public string NewName { get; init; } = "";

    [Option("mode", Description = "Alpaca account: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("watchlists rename", Summary = "Rename an Alpaca watchlist, keeping its symbols.", Risk = OperationRisk.Write)]
[Example("idiotproof watchlists rename tech megacaps")]
public sealed class WatchlistsRenameOperation : IOperation<WatchlistsRenameArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistsRenameArgs args, OperationContext ctx)
    {
        var newName = args.NewName.Trim();
        if (newName.Length == 0) return OperationResult.Usage("Give the new name, e.g. idiotproof watchlists rename tech megacaps.");
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var current = await AlpacaWatchlists.ResolveAsync(api, args.Watchlist, ctx.CancellationToken);
        // PUT replaces name AND symbols, so send the current symbols back unchanged.
        var w = await api.SendAsync(HttpMethod.Put, $"/v2/watchlists/{AlpacaWatchlists.Id(current)}",
            new { name = newName, symbols = AlpacaWatchlists.Symbols(current) }, null, ctx.CancellationToken);
        return OperationResult.Ok(AlpacaWatchlists.Shape(w, args.Mode), $"Renamed '{current.Str("name")}' to '{newName}'.\n" + AlpacaWatchlists.Render(w, args.Mode, ctx.Width));
    }
}

public sealed record WatchlistSymbolsArgs
{
    [Argument(0, Name = "watchlist", Description = "Watchlist id or name.")]
    public string Watchlist { get; init; } = "";

    [Argument(1, Name = "symbols", Description = "One or more symbols (space or comma separated).")]
    public string[] Symbols { get; init; } = [];

    [Option("mode", Description = "Alpaca account: paper (default) or live.")]
    public AlpacaAccount Mode { get; init; } = AlpacaAccount.Paper;
}

[Operation("watchlists add", Summary = "Add symbols to an Alpaca watchlist (by id or name).", Risk = OperationRisk.Write)]
[Example("idiotproof watchlists add tech AMD AVGO")]
public sealed class WatchlistsAddOperation : IOperation<WatchlistSymbolsArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistSymbolsArgs args, OperationContext ctx)
    {
        var symbols = AlpacaWatchlists.Normalize(args.Symbols).ToList();
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol to add.");
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var w = await AlpacaWatchlists.ResolveAsync(api, args.Watchlist, ctx.CancellationToken);
        var have = new HashSet<string>(AlpacaWatchlists.Symbols(w), StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        foreach (var symbol in symbols.Where(s => !have.Contains(s)))
        {
            w = await api.SendAsync(HttpMethod.Post, $"/v2/watchlists/{AlpacaWatchlists.Id(w)}", new { symbol }, null, ctx.CancellationToken);
            added.Add(symbol);
        }
        var skipped = symbols.Except(added).ToList();
        var note = (added.Count == 0 ? "Nothing added" : $"Added {string.Join(", ", added)}") +
                   (skipped.Count == 0 ? "." : $" (already there: {string.Join(", ", skipped)}).");
        return OperationResult.Ok(new { added, skipped, watchlist = AlpacaWatchlists.Shape(w, args.Mode) }, note + "\n" + AlpacaWatchlists.Render(w, args.Mode, ctx.Width));
    }
}

[Operation("watchlists remove", Summary = "Remove symbols from an Alpaca watchlist (by id or name).", Risk = OperationRisk.Write)]
[Example("idiotproof watchlists remove tech AMD")]
public sealed class WatchlistsRemoveOperation : IOperation<WatchlistSymbolsArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistSymbolsArgs args, OperationContext ctx)
    {
        var symbols = AlpacaWatchlists.Normalize(args.Symbols).ToList();
        if (symbols.Count == 0) return OperationResult.Usage("Name at least one symbol to remove.");
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var w = await AlpacaWatchlists.ResolveAsync(api, args.Watchlist, ctx.CancellationToken);
        var id = AlpacaWatchlists.Id(w);
        var have = new HashSet<string>(AlpacaWatchlists.Symbols(w), StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        foreach (var symbol in symbols.Where(have.Contains))
        {
            var after = await api.SendAsync(HttpMethod.Delete, $"/v2/watchlists/{id}/{Uri.EscapeDataString(symbol)}", null, null, ctx.CancellationToken);
            if (after.ValueKind == JsonValueKind.Object) w = after;
            removed.Add(symbol);
        }
        if (removed.Count > 0 && w.Items("assets").Any(a => removed.Contains(a.Str("symbol") ?? "", StringComparer.OrdinalIgnoreCase)))
            w = await api.GetAsync($"/v2/watchlists/{id}", null, ctx.CancellationToken); // Alpaca answered 204: re-read
        var missing = symbols.Except(removed).ToList();
        var note = (removed.Count == 0 ? "Nothing removed" : $"Removed {string.Join(", ", removed)}") +
                   (missing.Count == 0 ? "." : $" (not on the list: {string.Join(", ", missing)}).");
        return OperationResult.Ok(new { removed, missing, watchlist = AlpacaWatchlists.Shape(w, args.Mode) }, note + "\n" + AlpacaWatchlists.Render(w, args.Mode, ctx.Width));
    }
}

[Operation("watchlists delete", Summary = "Delete an Alpaca watchlist (by id or name).", Risk = OperationRisk.Destructive)]
[Example("idiotproof watchlists delete tech")]
public sealed class WatchlistsDeleteOperation : IOperation<WatchlistRefArgs>
{
    public async Task<OperationResult> ExecuteAsync(WatchlistRefArgs args, OperationContext ctx)
    {
        await using var api = await ReferenceSupport.TradingAsync(ctx, args.Mode);
        var w = await AlpacaWatchlists.ResolveAsync(api, args.Watchlist, ctx.CancellationToken);
        await api.SendAsync(HttpMethod.Delete, $"/v2/watchlists/{AlpacaWatchlists.Id(w)}", null, null, ctx.CancellationToken);
        var symbols = AlpacaWatchlists.Symbols(w);
        return OperationResult.Ok(new { deleted = AlpacaWatchlists.Shape(w, args.Mode) },
            $"Deleted Alpaca watchlist '{w.Str("name")}' ({symbols.Length} symbols{(symbols.Length > 0 ? ": " + string.Join(", ", symbols) : "")}).");
    }
}
