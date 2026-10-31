using System.Net;
using IdiotProof.Brokers;
using IdiotProof.Operations.Market;

namespace IdiotProof.Operations.Tests.Market;

/// <summary>market logo (IP-T323): the image bytes saved as SYMBOL.ext, path and size reported.</summary>
public class MarketLogoOperationTests
{
    private string dir = "";

    [SetUp]
    public void MakeDir() => dir = Path.Combine(Path.GetTempPath(), "ip-logo-test-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void RemoveDir()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task SavesTheImage_NamedBySymbolAndType()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var handler = new ScriptedMarketHandler().On("/v1beta1/logos/AAPL", png, "image/png");

        var result = await new MarketLogoOperation().ExecuteAsync(new MarketLogoArgs { Symbol = "aapl", Dir = dir }, MarketDataTestKit.Context(handler));

        Assert.That(result.IsOk, Is.True, result.Message);
        var path = Path.Combine(dir, "AAPL.png");
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(png));
        Assert.That(handler.Query("/v1beta1/logos/AAPL", "placeholder"), Is.EqualTo("false"));
        Assert.That(result.Human, Does.Contain(path).And.Contain("11 bytes"));
    }

    [Test]
    public void NoLogo_SurfacesAlpacasError()
    {
        var handler = new ScriptedMarketHandler().On("/v1beta1/logos/ZZZZ", """{"code":40410000,"message":"logo not found"}""", HttpStatusCode.NotFound);

        var ex = Assert.ThrowsAsync<AlpacaApiException>(() =>
            new MarketLogoOperation().ExecuteAsync(new MarketLogoArgs { Symbol = "ZZZZ", Dir = dir }, MarketDataTestKit.Context(handler)));

        Assert.That(ex!.AlpacaMessage, Is.EqualTo("logo not found"));
        Assert.That(Directory.Exists(dir), Is.False);
    }

    [Test]
    public void DefaultFolder_IsUnderLocalAppData() =>
        Assert.That(MarketLogoOperation.DefaultDir, Does.EndWith(Path.Combine("MindAttic", "IdiotProof", "Logos")));
}
