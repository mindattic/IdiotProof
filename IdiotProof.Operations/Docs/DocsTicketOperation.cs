using System.Globalization;
using System.Text;
using IdiotProof.Operations.Rendering;

namespace IdiotProof.Operations.Docs;

public sealed record DocsTicketArgs
{
    [Argument(0, Name = "id", Description = "The ticket: IP-T259, T259 or 259.")]
    public string Id { get; init; } = "";

    [Option("history", Description = "Also list every recorded change (status, priority, approval, links) with its ET time.")]
    public bool History { get; init; }
}

/// <summary>
/// <c>docs ticket</c> (IP-T269): one ticket from the ticket database (read-only, through
/// <c>tickets ticket show --json</c>): summary, status, description, dependencies, the commits
/// that name it and optionally its change history. Falls back to the USER_STORIES ticket table
/// when the tickets CLI is not available.
/// </summary>
[Operation("docs ticket", Summary = "Show one ticket: status, description, requires / required-by, the commits that name it, and its history.", Risk = OperationRisk.Read)]
[Example("idiotproof docs ticket IP-T259")]
[Example("idiotproof docs ticket 266 --history")]
public sealed class DocsTicketOperation : IOperation<DocsTicketArgs>
{
    public async Task<OperationResult> ExecuteAsync(DocsTicketArgs args, OperationContext ctx)
    {
        var id = KnowledgeIndex.TicketId(args.Id) ?? throw OperationException.Usage($"'{args.Id}' is not a ticket id. Try `docs ticket IP-T259`.");
        var knowledge = KnowledgeService.From(ctx);
        var detail = await knowledge.Tickets.ShowAsync(id, ctx.CancellationToken);
        if (detail is null)
        {
            var index = await knowledge.GetAsync(ct: ctx.CancellationToken);
            var row = index.Get(id) is { Kind: KnowledgeKind.Ticket } t ? t
                : throw DocsRender.NotFound("ticket", id, index, "The tickets CLI did not know it either.");
            return OperationResult.Ok(DocsRender.Data(row), DocsRender.Entry(row) + "\n\n(from docs/USER_STORIES.md; the tickets CLI was not available for details)");
        }

        var block = new KeyValueBlock()
            .Add("Status", detail.Status + (detail.Approval is { Length: > 0 } a ? $" · {a}" : ""))
            .Add("Type", detail.Type)
            .Add("Opened", detail.Date)
            .Add("Ref", detail.Ref is "" or "—" ? null : detail.Ref)
            .Add("Requires", detail.Requires.Count == 0 ? null : string.Join(", ", detail.Requires))
            .Add("Required by", detail.RequiredBy.Count == 0 ? null : string.Join(", ", detail.RequiredBy));

        var sb = new StringBuilder();
        sb.Append(detail.Id).Append(" — ").Append(detail.Summary).Append('\n').Append(block.Render().TrimEnd()).Append('\n');
        if (detail.Description.Length > 0) sb.Append('\n').Append(detail.Description.Trim()).Append('\n');
        foreach (var note in detail.Notes.Where(n => n.Length > 0)) sb.Append("\nNote: ").Append(note.Trim()).Append('\n');
        if (detail.Commits.Count > 0)
        {
            sb.Append("\nCommits:\n");
            var table = new TextTable { MaxWidth = ctx.Width }.Column("Commit").Column("Date").Column("Subject");
            foreach (var c in detail.Commits) table.Row(c.ShortHash, c.Date, c.Subject);
            sb.Append(table.Render()).Append('\n');
        }
        if (args.History && detail.History.Count > 0)
        {
            sb.Append("\nHistory:\n");
            var table = new TextTable { MaxWidth = ctx.Width }.Column("When (ET)").Column("Change");
            foreach (var h in detail.History) table.Row(Et(h.At, ctx), $"{h.Field}: {h.Old ?? ""} → {h.New ?? ""}".Replace(":  →", ": →"));
            sb.Append(table.Render()).Append('\n');
        }

        var data = new
        {
            detail.Id, detail.Summary, detail.Status, detail.Approval, detail.Type, opened = detail.Date, @ref = detail.Ref,
            detail.Description, detail.Requires, requiredBy = detail.RequiredBy, detail.Notes,
            commits = detail.Commits,
            history = detail.History.Select(h => new { atEt = Et(h.At, ctx), atUtc = h.At, h.Field, h.Old, h.New }),
        };
        return OperationResult.Ok(data, sb.ToString().TrimEnd());
    }

    /// <summary>The tickets CLI's UTC ISO timestamp as an ET time (the raw text when it does not parse).</summary>
    private static string Et(string isoUtc, OperationContext ctx) =>
        DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)
            ? ctx.Et(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            : isoUtc;
}
