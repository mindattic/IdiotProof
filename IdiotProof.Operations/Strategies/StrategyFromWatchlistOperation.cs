using System.Text.Json;
using IdiotProof.Core.Trading;
using IdiotProof.Operations.Rendering;
using IdiotProof.Scripting;

using IdiotProof.Core.Services;
namespace IdiotProof.Operations.Strategies;

public sealed record StrategyFromWatchlistArgs
{
    [Argument(0, Name = "file", Description = "A watchlist JSON file (absolute path: the Hub reads it), the JSON itself, or `-` for stdin: [{\"title\",\"symbol\",\"script\",\"description\",\"author\",\"originTranscript\",\"active\"}].")]
    public string File { get; init; } = "";

    [Option("mode", Description = "Broker mode for every new strategy: sandbox (default) or paper.", Short = 'm')]
    public BrokerMode Mode { get; init; } = BrokerMode.Sandbox;

    [Option("activate", Description = "Activate items marked \"active\": true (default: everything starts inactive).")]
    public bool Activate { get; init; }

    [Option("dry-run", Description = "Validate every item and report what would happen without saving.")]
    public bool DryRun { get; init; }
}

[Operation("strategy from-watchlist", Summary = "Create one strategy per item of a watchlist JSON file (the Monitor's create-strategies, through the CLI).", Risk = OperationRisk.Write,
    Description = "Each item's script is validated like `strategy create`; an item that fails is reported and skipped. Items already present (same symbol and title) or repeated in the file are skipped, " +
                  "so re-running is safe. New strategies start inactive in Sandbox with 1 share when unsized; --activate turns on the items marked active.")]
[Example("idiotproof strategy from-watchlist D:/watchlists/2026-10-30.json")]
[Example("idiotproof strategy from-watchlist -", Description = "Reads the watchlist JSON from stdin, e.g. `type watchlist.json | idiotproof strategy from-watchlist -`.")]
[Example("idiotproof strategy from-watchlist - --mode paper --activate --dry-run")]
public sealed class StrategyFromWatchlistOperation : IOperation<StrategyFromWatchlistArgs>
{
    /// <summary>One watchlist entry (the same shape MonitorCli create-strategies reads).</summary>
    public sealed class WatchlistItem
    {
        public string Title { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Script { get; set; } = "";
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? OriginTranscript { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>What happened to one item.</summary>
    public sealed record ItemOutcome(string Symbol, string Title, string Outcome, string? Detail, Guid? Id);

    public async Task<OperationResult> ExecuteAsync(StrategyFromWatchlistArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        StrategyDrafts.RequireStartMode(args.Mode);
        var items = Read(args.File);
        var repo = ctx.Get<StrategyRepository>();
        var existing = await repo.GetAllForUserAsync(owner, ctx.CancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<ItemOutcome>();

        foreach (var item in items)
        {
            var symbol = (item.Symbol ?? "").Trim().ToUpperInvariant();
            var title = string.IsNullOrWhiteSpace(item.Title) ? $"{symbol} strategy" : item.Title.Trim();
            if (symbol.Length == 0 || string.IsNullOrWhiteSpace(item.Script))
            { outcomes.Add(new(symbol, title, "skipped", "symbol or script missing", null)); continue; }
            if (!seen.Add($"{symbol}|{title}"))
            { outcomes.Add(new(symbol, title, "skipped", "repeated in the file", null)); continue; }
            if (existing.Any(e => e.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && StrategyArchive.Unmark(e.Title) == title))
            { outcomes.Add(new(symbol, title, "skipped", "already exists", null)); continue; }

            StrategyDefinition def;
            try { def = StrategyDrafts.FromScript(item.Script); }
            catch (OperationException ex) { outcomes.Add(new(symbol, title, "invalid", ex.Message, null)); continue; }
            StrategyDrafts.ApplyDefaults(def, symbol);
            if (!def.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            { outcomes.Add(new(symbol, title, "invalid", $"the script trades {def.Symbol}, the item says {symbol}", null)); continue; }
            var v = StrategyDrafts.Validate(def);
            if (v.HasErrors)
            { outcomes.Add(new(symbol, title, "invalid", string.Join("; ", v.Findings.Where(f => f.Level == FindingLevel.Error).Select(f => f.Message)), null)); continue; }

            var activate = args.Activate && item.Active;
            if (args.DryRun)
            { outcomes.Add(new(symbol, title, activate ? "would create + activate" : "would create", v.StampId, null)); continue; }

            var created = await StrategyDrafts.SaveAsync(ctx, def, title, args.Mode, "watchlist", item.Description, item.OriginTranscript, item.Author);
            var outcome = "created";
            if (activate)
            {
                var m = await repo.SetActiveAsync(created.Id, true, owner, ctx.CancellationToken);
                outcome = m == StrategyMutation.Ok ? "created + active" : $"created (activate: {m})";
            }
            outcomes.Add(new(symbol, title, outcome, v.StampId, created.Id));
        }

        var table = new TextTable { MaxWidth = ctx.Width }.Column("sym").Column("title", maxWidth: 32).Column("outcome").Column("detail", maxWidth: 60);
        foreach (var o in outcomes) table.Row(o.Symbol, o.Title, o.Outcome, o.Detail ?? "");
        var made = outcomes.Count(o => o.Outcome.StartsWith("created") || o.Outcome.StartsWith("would"));
        var human = (outcomes.Count == 0 ? "The watchlist has no items." : table.Render()) +
                    $"\n{(args.DryRun ? "Dry run: would create" : "Created")} {made}, skipped {outcomes.Count(o => o.Outcome == "skipped")}, invalid {outcomes.Count(o => o.Outcome == "invalid")} — {args.Mode}.";
        return OperationResult.Ok(new { DryRun = args.DryRun, Mode = args.Mode.ToString(), Items = outcomes }, human);
    }

    /// <summary>The argument as JSON text (when it looks like JSON) or as a file path.</summary>
    internal static List<WatchlistItem> Read(string source)
    {
        var text = (source ?? "").Trim();
        if (text.Length == 0) throw OperationException.Usage("Name a watchlist JSON file (or pass `-` and pipe the JSON in).");
        if (!text.StartsWith('[') && !text.StartsWith('{'))
        {
            if (!System.IO.File.Exists(text)) throw OperationException.Usage($"No such watchlist file: {text}");
            text = System.IO.File.ReadAllText(text);
        }
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return text.TrimStart().StartsWith('{')
                ? [JsonSerializer.Deserialize<WatchlistItem>(text, options)!]
                : JsonSerializer.Deserialize<List<WatchlistItem>>(text, options) ?? [];
        }
        catch (JsonException ex) { throw OperationException.Usage($"That watchlist isn't valid JSON: {ex.Message}"); }
    }
}
