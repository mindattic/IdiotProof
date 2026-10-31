using IdiotProof.Core.Data;
using IdiotProof.Core.Identity;
using IdiotProof.Core.Ledger;

using IdiotProof.Core.Services;
namespace IdiotProof.Blazor.Services;

/// <summary>
/// The Activity page's "Commands" list: the signed-in owner's <see cref="CommandLedgerEntry"/> rows,
/// newest first, so what the CLI or an assistant over MCP told IdiotProof to do shows up next to
/// the Monitor's events. Arguments arrive already redacted (<see cref="CommandLedgerRepository"/>).
/// Only the owner's own rows are ever read; with nobody signed in the list is empty.
/// </summary>
public sealed class CommandActivityService(CommandLedgerRepository ledger, IOwnerContext owner)
{
    /// <summary>The headless front doors this list is for by default.</summary>
    public static readonly IReadOnlyList<string> HeadlessCallers = ["cli", "mcp"];

    /// <summary>
    /// Up to <paramref name="take"/> of the owner's ledger rows. By default only those from the CLI
    /// and MCP; <paramref name="includeAllCallers"/> also shows web and test rows.
    /// </summary>
    public async Task<IReadOnlyList<CommandLedgerEntry>> LoadAsync(
        bool includeAllCallers = false, int take = 100, CancellationToken ct = default)
    {
        if (await owner.GetOwnerIdAsync(ct) is not { } ownerId) return [];

        // Over-fetch so filtering out web rows still leaves a full page in the common case.
        var rows = await ledger.ListAsync(ownerId, take: includeAllCallers ? take : take * 3, ct: ct);
        return rows
            .Where(r => r.OwnerUserId == ownerId)
            .Where(r => includeAllCallers || HeadlessCallers.Contains(r.Caller, StringComparer.OrdinalIgnoreCase))
            .Take(take)
            .ToList();
    }

    /// <summary>"cli" → "CLI", "mcp" → "MCP", anything else title-cased for the badge.</summary>
    public static string CallerLabel(string caller) => caller.ToLowerInvariant() switch
    {
        "cli" => "CLI",
        "mcp" => "MCP",
        "blazor" => "Web",
        "" => "?",
        var other => char.ToUpperInvariant(other[0]) + other[1..],
    };

    /// <summary>Badge level for a ledger status: Ok is INFO, a refusal or pending confirm WARN, a failure ERROR.</summary>
    public static string StatusLevel(string status) => status switch
    {
        "Ok" => "INFO",
        "Blocked" or "ConfirmationRequired" or "UsageError" => "WARN",
        _ => "ERROR",
    };
}
