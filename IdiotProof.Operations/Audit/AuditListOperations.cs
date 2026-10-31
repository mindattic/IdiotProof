using Microsoft.EntityFrameworkCore;

namespace IdiotProof.Operations.Audit;

public sealed record AuditListArgs : AuditFilterArgs;

/// <summary>
/// The audit trail, newest first: signal fires and vetoes, entries and exits, rejected orders,
/// risk-guardian blocks, broker switches and system events, for you (plus system-wide rows).
/// </summary>
[Operation("audit list", Summary = "The audit trail, newest first: signals, entries/exits, rejections, risk blocks, system events (ET).", Risk = OperationRisk.Read)]
[Example("idiotproof audit list")]
[Example("idiotproof audit list --since 2026-10-30 --kind signal-fire,entry,exit")]
[Example("idiotproof audit list --kind order-rejected --no-system --limit 200")]
public sealed class AuditListOperation : IOperation<AuditListArgs>
{
    public async Task<OperationResult> ExecuteAsync(AuditListArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        AuditSupport.Validate(args);
        await using var db = await AuditSupport.OpenAsync(ctx);
        var rows = (await AuditSupport.Query(db, owner, args)
                .OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
                .Take(args.Limit).ToListAsync(ctx.CancellationToken))
            .Select(AuditSupport.ToRow).ToList();

        var kinds = AuditSupport.Kinds(args);
        var head = "Audit trail" + (args.Since is { } s ? $" since {s:yyyy-MM-dd}" : "") + (kinds.Count > 0 ? $" ({string.Join(",", kinds)})" : "");
        if (rows.Count == 0) return OperationResult.Ok(new { Rows = rows }, $"{head}\nNo audit rows match.");
        return OperationResult.Ok(new { Rows = rows },
            $"{head}\n{AuditSupport.Table(rows, ctx.Width)}\n{rows.Count} rows{AuditSupport.Legend(rows)}. `idiotproof audit search <text>` finds one.");
    }
}

public sealed record AuditSearchArgs : AuditFilterArgs
{
    [Argument(0, Name = "text", Description = "Text to find in the message or the structured payload (case-insensitive).")]
    public string Text { get; init; } = "";
}

/// <summary>
/// Searches the audit trail for text in each row's message or its structured payload (order
/// requests, broker responses, veto reasons): a symbol, an order id, a strategy title.
/// </summary>
[Operation("audit search", Summary = "Find audit rows whose message or payload contains text: a symbol, order id, strategy title.", Risk = OperationRisk.Read)]
[Example("idiotproof audit search NVDA")]
[Example("idiotproof audit search \"insufficient buying power\" --since 2026-10-01")]
public sealed class AuditSearchOperation : IOperation<AuditSearchArgs>
{
    public async Task<OperationResult> ExecuteAsync(AuditSearchArgs args, OperationContext ctx)
    {
        var owner = ctx.RequireOwner();
        AuditSupport.Validate(args);
        var text = (args.Text ?? "").Trim();
        if (text.Length < 2) return OperationResult.Usage("Search for at least 2 characters.");

        await using var db = await AuditSupport.OpenAsync(ctx);
        // ToLower on both sides: SQL Server's collation is case-insensitive anyway, EF InMemory is not.
        var needle = text.ToLowerInvariant();
        var rows = (await AuditSupport.Query(db, owner, args)
                .Where(a => a.Message.ToLower().Contains(needle) || (a.DataJson != null && a.DataJson.ToLower().Contains(needle)))
                .OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
                .Take(args.Limit).ToListAsync(ctx.CancellationToken))
            .Select(AuditSupport.ToRow).ToList();

        var data = new { Text = text, Rows = rows };
        if (rows.Count == 0) return OperationResult.Ok(data, $"No audit rows mention \"{text}\".");
        var inPayloadOnly = rows.Count(r => !r.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
        return OperationResult.Ok(data,
            $"Audit rows mentioning \"{text}\":\n{AuditSupport.Table(rows, ctx.Width)}\n{rows.Count} rows{AuditSupport.Legend(rows)}"
            + (inPayloadOnly > 0 ? $"; {inPayloadOnly} matched only in the payload (--json shows it)." : "."));
    }
}
