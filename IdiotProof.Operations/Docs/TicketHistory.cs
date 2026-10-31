using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IdiotProof.Operations.Docs;

/// <summary>Runs the workspace's <c>tickets</c> CLI and returns its standard output (null when it failed or is absent).</summary>
public interface ITicketCli
{
    Task<string?> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct);
}

/// <summary>The real <c>tickets</c> process (a dotnet global tool on PATH), with a timeout. Read-only use only.</summary>
public sealed class TicketsProcess : ITicketCli
{
    /// <summary>Overrides the executable (a full path when PATH lacks the dotnet tools folder).</summary>
    public const string ExeEnvVar = "IDIOTPROOF_TICKETS_EXE";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<string?> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable(ExeEnvVar) is { Length: > 0 } exe ? exe : "tickets")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        psi.Environment["NO_COLOR"] = "1";

        Process? process;
        try { process = Process.Start(psi); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
        if (process is null) return null;

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(Timeout);
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                _ = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0 ? await stdout : null;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) throw;
                return null;
            }
        }
    }
}

/// <summary>One commit that names a ticket.</summary>
public sealed record TicketCommit(string ShortHash, string Date, string Subject);

/// <summary>A ticket's change history line (status, priority, approval, link…), timestamp in UTC as given.</summary>
public sealed record TicketEvent(string At, string Field, string? Old, string? New);

/// <summary>Everything <c>tickets ticket show --json</c> says about one ticket that the docs care about.</summary>
public sealed record TicketDetail(
    string Id,
    string Date,
    string Type,
    string Status,
    string Summary,
    string Ref,
    string Description,
    string? Approval,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> RequiredBy,
    IReadOnlyList<string> Notes,
    IReadOnlyList<TicketCommit> Commits,
    IReadOnlyList<TicketEvent> History);

/// <summary>
/// IdiotProof's ticket history through the <c>tickets</c> CLI (IP-T264). Strictly read-only: it
/// only ever runs <c>ticket list</c> and <c>ticket show</c> with <c>--json</c>.
/// </summary>
public sealed class TicketHistory(ITicketCli cli, string repo = "IdiotProof")
{
    public string Repo { get; } = repo;

    /// <summary>Every ticket of the repo (one line each), or null when the CLI could not be run.</summary>
    public async Task<IReadOnlyList<KnowledgeEntry>?> ListAsync(CancellationToken ct)
    {
        var json = await cli.RunAsync(["ticket", "list", "--repo", Repo, "--json"], ct);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return [.. doc.RootElement.EnumerateArray().Select(t =>
            {
                var id = Str(t, "id");
                var summary = Str(t, "summary");
                var refText = Str(t, "ref");
                var body = $"{summary}\nType: {Str(t, "type")} · Opened: {Str(t, "date")} · Status: {Str(t, "status")}" +
                           (refText is "" or "—" ? "" : $" · Ref: {refText}");
                return new KnowledgeEntry(KnowledgeKind.Ticket, id, KnowledgeIndexBuilder.Headline(summary), body, "tickets", 0)
                {
                    Status = Str(t, "status"),
                };
            })];
        }
        catch (JsonException) { return null; }
    }

    /// <summary>One ticket in full, or null when it does not exist or the CLI could not be run.</summary>
    public async Task<TicketDetail?> ShowAsync(string id, CancellationToken ct)
    {
        var json = await cli.RunAsync(["ticket", "show", id, "--json"], ct);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Reads a <c>ticket show --json</c> object.</summary>
    public static TicketDetail Parse(JsonElement t)
    {
        var id = Str(t, "id");
        var links = Array(t, "links").ToList();
        return new TicketDetail(
            id,
            Str(t, "date"),
            Str(t, "type"),
            Str(t, "status"),
            Str(t, "summary"),
            Str(t, "ref"),
            Str(t, "description"),
            t.TryGetProperty("approval", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null,
            [.. Array(t, "requires").Select(r => r.GetString() ?? "")],
            [.. links.Where(l => Str(l, "kind") == "requires" && Str(l, "to") == id).Select(l => Str(l, "from"))],
            [.. Array(t, "notes").Select(n => n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : Str(n, "text"))],
            [.. Array(t, "commits").Select(c => new TicketCommit(Str(c, "shortHash"), Str(c, "date"), Str(c, "subject")))],
            [.. Array(t, "history").Select(h => new TicketEvent(Str(h, "at"), Str(h, "field"), Opt(h, "old"), Opt(h, "new")))]);
    }

    /// <summary>IP-T264: adds every ticket the CLI knows that the USER_STORIES table did not already list.</summary>
    public async Task IndexIntoAsync(KnowledgeIndex index, CancellationToken ct)
    {
        var tickets = await ListAsync(ct);
        if (tickets is null)
        {
            index.Warn("tickets CLI not available; ticket history comes from docs/USER_STORIES.md only");
            return;
        }
        foreach (var t in tickets)
        {
            if (index.Get(t.Id) is null) index.Add(t);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? Opt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
}
