using IdiotProof.Core.History;
using IdiotProof.Operations.Rendering;
using Microsoft.Data.SqlClient;

namespace IdiotProof.Operations.History;

public sealed record HistoryStatusArgs;

[Operation("history status", Summary = "The permanent market history database (IP-T454): its server and size, rows per table (minute bars, daily bars, splits, news, filings), symbols covered per kind, the newest coverage, and the backfill queue.", Risk = OperationRisk.Read,
    Description = "Reads the history database on the local SQL Server (ConnectionStrings__IdiotProofHistory, default localhost/IdiotProof_History). Creates the schema if it is missing; never fetches data.")]
[Example("idiotproof history status")]
public sealed class HistoryStatusOperation : IOperation<HistoryStatusArgs>
{
    public async Task<OperationResult> ExecuteAsync(HistoryStatusArgs args, OperationContext ctx)
    {
        var h = ctx.Get<HistoryDatabase>();
        try
        {
            var applied = await h.EnsureSchemaAsync(ctx.CancellationToken);
            var status = await HistoryStatus.ReadAsync(h, ctx.CancellationToken);
            var kv = new KeyValueBlock()
                .Add("database", status.Database)
                .Add("size", $"{status.SizeGb:0.00} GB")
                .Add("schema", applied.Count == 0 ? $"version {status.SchemaVersion}" : $"version {status.SchemaVersion} (applied now: {string.Join("; ", applied)})");
            foreach (var (table, rows) in status.Rows) kv.Add(table, rows.ToString("N0"));
            foreach (var (kind, symbols) in status.SymbolsByKind) kv.Add($"symbols with {kind}", symbols.ToString("N0"));
            kv.Add("backfill queue", string.Join(", ", status.Jobs.Select(j => $"{j.Key} {j.Value}")), "empty");
            return OperationResult.Ok(status, kv.Render());
        }
        catch (SqlException ex)
        {
            return OperationResult.Blocked($"History database unreachable ({h.DatabaseName}): {ex.Message}");
        }
    }
}

public sealed record HistoryImportArgs
{
    [Option("research", Description = "Import the gapper study's research database (daily bars, splits, news, filings, coverage ranges).")]
    public bool Research { get; init; }

    [Option("research-db", Description = "Research database name on LocalDB (default IdiotProof_Research_GAP).")]
    public string ResearchDb { get; init; } = "IdiotProof_Research_GAP";

    [Option("minutes", Description = "Import the 1-minute file store (every covered day, zero-bar days included).")]
    public bool Minutes { get; init; }

    [Option("bar-root", Description = @"Minute-bar file store root (default D:\IdiotProofData\gapper-study\bars).")]
    public string BarRoot { get; init; } = @"D:\IdiotProofData\gapper-study\bars";

    [Option("symbols", Description = "Minute import only these symbols (comma-separated); default every symbol in the store.")]
    public string? Symbols { get; init; }

    [Option("parallelism", Description = "Symbols imported at once (default 4).")]
    public int Parallelism { get; init; } = 4;
}

[Operation("history import", Summary = "Move data already downloaded into the permanent history database so it is never fetched again: the gapper study's research database and its 1-minute file store. Idempotent: rows already held are skipped.", Risk = OperationRisk.Write,
    Description = "Writes only to the history database; reads the research database and the file store; makes no API call.")]
[Example("idiotproof history import --research --minutes")]
public sealed class HistoryImportOperation : IOperation<HistoryImportArgs>
{
    public async Task<OperationResult> ExecuteAsync(HistoryImportArgs args, OperationContext ctx)
    {
        if (!args.Research && !args.Minutes) return OperationResult.Usage("Pass --research, --minutes or both.");
        var h = ctx.Get<HistoryDatabase>();
        await h.EnsureSchemaAsync(ctx.CancellationToken);
        var importer = new HistoryImporter(h, m => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}"));
        var results = new List<HistoryImportResult>();
        if (args.Research)
        {
            var research = $@"Server=(localdb)\MSSQLLocalDB;Database={args.ResearchDb};Trusted_Connection=True;TrustServerCertificate=True;Command Timeout=0;";
            results.AddRange(await importer.ImportResearchAsync(research, ctx.CancellationToken));
        }
        if (args.Minutes)
        {
            var only = string.IsNullOrWhiteSpace(args.Symbols) ? null : args.Symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            results.Add(await importer.ImportMinuteFilesAsync(args.BarRoot, args.Parallelism, ctx.CancellationToken, only));
        }
        var table = new TextTable { MaxWidth = ctx.Width }.Column("step").Column("inserted", Align.Right).Column("skipped", Align.Right).Column("seconds", Align.Right).Column("note");
        foreach (var r in results) table.Row(r.Step, r.Rows.ToString("N0"), r.Skipped.ToString("N0"), r.Seconds.ToString("0.0"), r.Note ?? "");
        return OperationResult.Ok(results, table.Render());
    }
}
