using MindAttic.Vault.Credentials;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// Points the MindAttic LLM keyring at an empty temp directory for the lifetime of the scope, so
/// <c>AppSettings.ClaudeApiKey</c> resolves no host key even on a machine whose real Vault holds one.
/// Restores the previous override on dispose.
/// </summary>
internal sealed class EmptyLlmVault : IDisposable
{
    private readonly string? previous = Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar);
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"ip-llm-empty-{Guid.NewGuid():N}");

    public EmptyLlmVault()
    {
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, previous);
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
