using System.Security.Cryptography;
using System.Text;
using IdiotProof.Engine.Settings;
using IdiotProof.Engine.Storage;

namespace IdiotProof.Frontend.Auth;

/// <summary>
/// Single-owner password authentication. The password hash is stored in AppSettings.
/// On first run (no password set), any password is accepted and saved immediately.
/// </summary>
public sealed class AuthService
{
    private readonly AppSettings settings;
    private readonly IStorageProvider storage;

    public AuthService(AppSettings settings, IStorageProvider storage)
    {
        this.settings = settings;
        this.storage  = storage;
    }

    public bool IsPasswordSet => !string.IsNullOrWhiteSpace(settings.AdminPasswordHash);

    public bool ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;

        // First-run: no password set — accept and save whatever is entered
        if (!IsPasswordSet)
        {
            SetPassword(password);
            return true;
        }

        return HashPassword(password) == settings.AdminPasswordHash;
    }

    public void SetPassword(string newPassword)
    {
        settings.AdminPasswordHash = HashPassword(newPassword);
        settings.Save(storage);
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("IdiotProof:v1:" + password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
