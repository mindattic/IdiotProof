using System.Collections.Concurrent;

namespace IdiotProof.Core.Ledger;

/// <summary>
/// The Hub operation running on this async flow, so what it causes can be tied back to its
/// <see cref="IdiotProof.Blazor.Data.CommandLedgerEntry"/>. The operation dispatcher opens one
/// (synchronously, so it is visible to the Hub code that later appends the ledger row); the
/// TransactionGateway records each audit row it writes into it; and
/// <see cref="CommandLedgerRepository.AppendAsync"/>, once the ledger row has its id, stamps
/// <c>command_ledger_id</c> onto those audit rows. The ledger is written after the operation
/// ends (it carries the status and duration), which is why the link is made in that order.
/// </summary>
public sealed class CommandScope
{
    private static readonly AsyncLocal<CommandScope?> current = new();
    private readonly ConcurrentQueue<long> auditIds = new();

    private CommandScope(string operation, string caller)
    {
        Operation = operation;
        Caller = caller;
    }

    /// <summary>The scope on this flow, or null outside a dispatched operation (the Monitor, the web app).</summary>
    public static CommandScope? Current => current.Value;

    /// <summary>Catalog name of the operation, e.g. "orders place".</summary>
    public string Operation { get; }

    /// <summary>"cli", "mcp", "blazor" or "test".</summary>
    public string Caller { get; }

    /// <summary>Audit rows written for this operation, in order.</summary>
    public IReadOnlyList<long> AuditIds => auditIds.ToArray();

    /// <summary>The ledger row these were linked to, once linked (a scope links once).</summary>
    public long? LedgerId { get; internal set; }

    /// <summary>
    /// Opens a scope for <paramref name="operation"/> on the current flow. Call it from a
    /// non-async method so the value stays visible to the caller after it returns.
    /// </summary>
    public static CommandScope Begin(string operation, string caller)
    {
        var scope = new CommandScope(operation, caller);
        current.Value = scope;
        return scope;
    }

    /// <summary>Records an audit row this operation caused.</summary>
    public void LinkAudit(long auditId) => auditIds.Enqueue(auditId);
}
