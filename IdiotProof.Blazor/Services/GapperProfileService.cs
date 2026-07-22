using System.Text.Json;
using IdiotProof.Scripting;

namespace IdiotProof.Blazor.Services;

/// <summary>
/// Loads the gapper profile catalog — the first static JSON catalog under
/// IP-LAW-7 (JSON for static data). Profiles are templates: the Gapper tab
/// clones one per ticker and the user dials the copy in; nothing writes back
/// to the catalog file.
/// </summary>
public sealed class GapperProfileService(IWebHostEnvironment env, ILogger<GapperProfileService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private IReadOnlyList<GapperProfile>? cache;

    public IReadOnlyList<GapperProfile> GetProfiles()
    {
        if (cache is not null) return cache;

        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var path = Path.Combine(webRoot, "data", "gapper-profiles.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var profiles = doc.RootElement.GetProperty("profiles")
                .Deserialize<List<GapperProfile>>(JsonOpts) ?? [];
            cache = profiles;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load gapper profile catalog from {Path} — serving a single built-in default.", path);
            cache = [new GapperProfile { Id = "classic-gapper", Name = "Classic Gapper", Description = "Built-in fallback profile." }];
        }
        return cache;
    }

    public GapperProfile? GetById(string id) =>
        GetProfiles().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
