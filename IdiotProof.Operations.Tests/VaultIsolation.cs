// No namespace on purpose: a [SetUpFixture] covers only its own namespace, so this one covers the whole assembly.
/// <summary>
/// IP-T347: AppSettings.Load/Save read and write MindAttic Vault. Point Vault at temp folders for the whole assembly
/// so no test touches the real %APPDATA%\MindAttic or %LOCALAPPDATA%\MindAttic.
/// </summary>
[SetUpFixture]
public sealed class IdiotProofVaultIsolation
{
    private string root = "";

    [OneTimeSetUp]
    public void Isolate()
    {
        root = Path.Combine(Path.GetTempPath(), "idiotproof-vault-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MINDATTIC_VAULT_ROAMING_ROOT", Path.Combine(root, "roaming"));
        Environment.SetEnvironmentVariable("MINDATTIC_VAULT_LOCAL_ROOT", Path.Combine(root, "local"));
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_VAULT_ROAMING_ROOT", null);
        Environment.SetEnvironmentVariable("MINDATTIC_VAULT_LOCAL_ROOT", null);
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
