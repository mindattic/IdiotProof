using IdiotProof.Operations.Gapper;

namespace IdiotProof.Operations.Tests.Gapper;

/// <summary>gapper profile show (IP-T163): every dial of one profile, by id or by name.</summary>
public class GapperProfileShowOperationTests
{
    [Test]
    public async Task ShowsTheDials_AndTheScriptItGenerates()
    {
        using var kit = new GapperKit();
        var result = await new GapperProfileShowOperation().ExecuteAsync(new GapperProfileShowArgs { Name = "penny-runner" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("Penny Runner").And.Contain("10-60%").And.Contain("PeakGiveback(").And.Contain("Ticker(\"TICK\")"));
    }

    [Test]
    public async Task FindsAProfileByItsDisplayName()
    {
        using var kit = new GapperKit();
        var result = await new GapperProfileShowOperation().ExecuteAsync(new GapperProfileShowArgs { Name = "Classic Gapper" }, kit.Context());

        Assert.That(result.IsOk, Is.True, result.Message);
        Assert.That(result.Human, Does.Contain("classic-gapper"));
    }

    [Test]
    public void AnUnknownProfile_IsAUsageErrorListingTheCatalog()
    {
        using var kit = new GapperKit();
        var ex = Assert.ThrowsAsync<OperationException>(() => new GapperProfileShowOperation().ExecuteAsync(new GapperProfileShowArgs { Name = "nope" }, kit.Context()));

        Assert.That(ex!.Status, Is.EqualTo(OperationStatus.UsageError));
        Assert.That(ex.Message, Does.Contain("classic-gapper"));
    }
}
