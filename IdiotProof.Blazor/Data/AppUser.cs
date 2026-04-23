using Microsoft.AspNetCore.Identity;

namespace IdiotProof.Blazor.Data;

/// <summary>
/// Application user — extends IdentityUser with no extra fields for now.
/// </summary>
public sealed class AppUser : IdentityUser
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
