// ============================================================================
// ProjectNames - Constants for project identifiers used in settings
// ============================================================================
//
// NOMENCLATURE:
// - Project: A distinct component of the IdiotProof solution
// - Backend: IB (Interactive Brokers) connection and trading logic
// - Frontend: MAUI UI application for strategy management
// - Console: CLI interface for script editing and testing
// - Shared: Common models, validation, and IdiotScript parsing
//
// PROJECT RESPONSIBILITIES:
// - Backend: Trading execution, IB API integration, strategy running
// - Frontend: Visual strategy editor, monitoring dashboard
// - Console: Text-based IdiotScript editing, batch operations
// - Shared: IdiotScript parsing, validation, models, enums
//
// ============================================================================

namespace IdiotProof.Shared.Settings;

/// <summary>
/// Constants for project names used in settings file paths.
/// </summary>
public static class ProjectNames
{
    /// <summary>Backend project - IB connection and trading logic.</summary>
    public const string Backend = "Backend";

    /// <summary>Backend unit tests project.</summary>
    public const string BackendUnitTests = "Backend.UnitTests";

    /// <summary>Console project - CLI interface for strategy management.</summary>
    public const string Console = "Console";

    /// <summary>Console unit tests project.</summary>
    public const string ConsoleUnitTests = "Console.UnitTests";

    /// <summary>Frontend project - MAUI UI application.</summary>
    public const string Frontend = "Frontend";

    /// <summary>Shared library project.</summary>
    public const string Shared = "Shared";
}
