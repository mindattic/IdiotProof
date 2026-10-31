using IdiotProof.Operations.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Audit;

public sealed record LedgerListArgs
{
    [Option("op", Description = "Only this operation (\"strategy create\" or strategy_create), or a noun for all of its verbs (strategy).")]
    public string? Op { get; init; }

    [Option("since", Description = "First ET date to include, yyyy-MM-dd (default: everything).")]
    public DateOnly? Since { get; init; }

    [Option("caller", Description = "Only calls from this front door: cli, mcp, blazor or test.")]
    public string? Caller { get; init; }

    [Option("status", Description = "Only this outcome: ok, failed, usage-error, blocked or confirmation-required.")]
    public string? Status { get; init; }

    [Option("limit", Description = "Most rows, newest first, 1-1000 (default 50).")]
    public int Limit { get; init; } = 50;
}

/// <summary>One command-ledger row as <c>ledger list</c> returns it (arguments are already redacted).</summary>
public sealed record LedgerRow(long Id, DateTime TimestampUtc, string TimeEt, string Operation, string Caller, string Status,
    int ExitCode, int DurationMs, string? Message, string? ArgsJson);

/// <summary>
/// The command ledger: every operation the Hub ran for you — from the CLI, an MCP agent or the
/// web app — with its (secret-redacted) arguments, how it ended and how long it took. The answer
/// to "what did I, or the assistant, tell IdiotProof to do?".
/// </summary>
[Operation("ledger list", Summary = "Every command the Hub ran for you (CLI, MCP, web): operation, arguments, outcome, duration (ET).", Risk = OperationRisk.Read)]
[Example("idiotproof ledger list")]
[Example("idiotproof ledger list --op \"order place\" --since 2026-10-30")]
[Example("idiotproof ledger list --op strategy --caller mcp --status blocked")]
public sealed class LedgerListOperation : IOperation<LedgerListArgs>
{
    private static readonly string[] Statuses = Enum.GetNames<OperationStatus>();

    public async Task<OperationResult> ExecuteAsync(LedgerListArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        if (args.Limit is < 1 or > 1000) return OperationResult.Usage("--limit must be 1-1000.");
        var caller = string.IsNullOrWhiteSpace(args.Caller) ? null : args.Caller.Trim().ToLowerInvariant();
        if (caller is not (null or "cli" or "mcp" or "blazor" or "test")) return OperationResult.Usage("--caller is cli, mcp, blazor or test.");
        string? status = null;
        if (!string.IsNullOrWhiteSpace(args.Status))
        {
            status = Statuses.FirstOrDefault(s => OperationNames.Fold(s) == OperationNames.Fold(args.Status));
            if (status is null) return OperationResult.Usage("--status is ok, failed, usage-error, blocked or confirmation-required.");
        }
        var op = string.IsNullOrWhiteSpace(args.Op) ? null : string.Join(' ', args.Op.Trim().ToLowerInvariant().Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var nounOnly = op is not null && !op.Contains(' ');

        await using var db = await AuditSupport.OpenAsync(ctx);
        var q = db.CommandLedger.AsNoTracking().Where(l => l.OwnerUserId == owner);
        if (op is not null)
        {
            var prefix = op + " ";
            q = nounOnly ? q.Where(l => l.Operation == op || l.Operation.StartsWith(prefix)) : q.Where(l => l.Operation == op);
        }
        if (args.Since is { } since) { var from = Account.AccountApis.EtMidnightUtc(since); q = q.Where(l => l.TimestampUtc >= from); }
        // Callers are stored lower-case, statuses as the enum name (CommandLedgerEntry); match either spelling.
        if (caller is not null) q = q.Where(l => l.Caller.ToLower() == caller);
        if (status is not null) q = q.Where(l => l.Status == status);

        var rows = (await q.OrderByDescending(l => l.TimestampUtc).ThenByDescending(l => l.Id).Take(args.Limit).ToListAsync(ctx.CancellationToken))
            .Select(l => new LedgerRow(l.Id, l.TimestampUtc, Fmt.Et(l.TimestampUtc), l.Operation, l.Caller.ToLowerInvariant(), l.Status,
                l.ExitCode, l.DurationMs, l.Message, l.ArgsJson))
            .ToList();

        var filters = new List<string>();
        if (op is not null) filters.Add(nounOnly ? $"{op} *" : op);
        if (args.Since is { } s) filters.Add($"since {s:yyyy-MM-dd}");
        if (caller is not null) filters.Add(caller);
        if (status is not null) filters.Add(status);
        var head = "Command ledger" + (filters.Count > 0 ? $" ({string.Join(", ", filters)})" : "");
        var data = new { Rows = rows };
        if (rows.Count == 0) return OperationResult.Ok(data, $"{head}\nNo commands match.");

        var table = new TextTable { MaxWidth = ctx.Width }
            .Column("id", Align.Right).Column("when (ET)").Column("via").Column("operation").Column("status")
            .Column("ms", Align.Right).Column("args", maxWidth: 50).Column("message", maxWidth: 40);
        foreach (var r in rows)
            table.Row(r.Id.ToString(), r.TimeEt, r.Caller, r.Operation, r.Status, r.DurationMs.ToString(), r.ArgsJson ?? "", r.Message ?? "");
        var failed = rows.Count(r => r.Status != nameof(OperationStatus.Ok));
        return OperationResult.Ok(data, $"{head}\n{table.Render()}\n{rows.Count} commands, {failed} not ok.");
    }
}
