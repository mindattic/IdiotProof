namespace IdiotProof.Blazor.Services;

/// <summary>
/// Carries dev autofill credentials read from <c>.env</c> at startup. Both fields
/// are <c>null</c> in non-Development environments — the Login page reads them
/// and prefills its inputs only when at least <see cref="Username"/> is set, so
/// production builds render the form empty.
/// </summary>
public sealed record DevCredentials(string? Username, string? Password);
