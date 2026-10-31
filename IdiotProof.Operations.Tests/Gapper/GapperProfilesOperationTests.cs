using IdiotProof.Operations.Gapper;
using IdiotProof.Scripting;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper profiles (IP-T162): the static profile catalog, one row per template.</summary>
public class GapperProfilesOperationTests
{
    [Test]
    public async Task ListsEveryCatalogProfile_WithItsDials()
    {
        using var kit = new GapperKit();
        var result = await new GapperProfilesOperation().ExecuteAsync(new NoArgs(), kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        var profiles = (IReadOnlyList<GapperProfile>)result.Data!;
        Assert.That(profiles.Select(p => p.Id), Is.SupersetOf(new[] { "classic-gapper", "penny-runner", "large-cap-gap" }));
        Assert.That(result.Human, Does.Contain("classic-gapper").And.Contain("≥5%").And.Contain("09:28").And.Contain("profiles."));
    }
}
