namespace IdiotProof.Engine.Workspace;

/// <summary>
/// Persistence abstraction for workspaces. Lets the Engine's
/// <see cref="WorkspaceManager"/> stay storage-agnostic while the host (Blazor,
/// CLI, Monitor, future supervisors) chooses an implementation:
///
///   • <see cref="JsonFileWorkspaceStore"/> — legacy on-disk JSON files at
///     <c>%LOCALAPPDATA%\MindAttic\IdiotProof\Workspaces\{userId}\*.json</c>.
///     The default registration in <c>ServiceRegistration.AddIdiotProofEngine</c>.
///   • <c>SqlWorkspaceStore</c> in IdiotProof.Blazor — SQL-backed via
///     <c>WorkspaceRepository</c>. Blazor overrides the default registration
///     with this one so user-edited workspaces land in the same database the
///     rest of the user-state lives in.
///
/// On startup, the SQL store performs a one-shot import from the JSON dir if
/// SQL has no rows but disk files exist, so the migration is invisible to
/// users with pre-existing workspaces.
///
/// All methods are pure data ops — no caching. <see cref="WorkspaceManager"/>
/// owns the in-memory cache layer.
/// </summary>
public interface IWorkspaceStore
{
    /// <summary>Loads every workspace tab for a user. Empty list when user has none.</summary>
    IReadOnlyList<WorkspaceTab> Load(string userId);

    /// <summary>Upserts a tab for a user.</summary>
    void Save(string userId, WorkspaceTab tab);

    /// <summary>Removes a tab. Returns true when something was actually deleted.</summary>
    bool Delete(string userId, string tabId);

    /// <summary>
    /// All user ids that have at least one workspace persisted. Used by the
    /// global iteration paths (StrategyExecutionService, CLI <c>workspaces</c>).
    /// The legacy <c>__global__</c> bucket may or may not appear depending on
    /// implementation.
    /// </summary>
    IEnumerable<string> EnumerateUserIds();
}
