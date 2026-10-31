using System.Text.RegularExpressions;

namespace IdiotProof.Blazor.Tests;

/// <summary>
/// The web app's order paths go through the TransactionGateway. These read the page sources the
/// app is built from, so a later edit that calls a broker client directly fails here first.
/// </summary>
[TestFixture]
public sealed class BlazorOrderPathTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdiotProof.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (IdiotProof.slnx) not found.");
    }

    /// <summary>
    /// IP-T282: the Options page places through OptionsTradingService.PlaceOrderAsync (the
    /// gateway), handing it the elevation service so the server checks the password window —
    /// and the page keeps its own password prompt before a Live ticket.
    /// </summary>
    [Test]
    public void OptionsPage_PlacesThroughTheGateway_AndKeepsPasswordElevation()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "IdiotProof.Blazor", "Components", "Pages", "Options.razor"));

        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain("OptionsSvc.PlaceOrderAsync("), "orders go through the gateway");
            Assert.That(Regex.IsMatch(page, @"OptionsSvc\.PlaceOrderAsync\([^;]*ElevationSvc\)", RegexOptions.Singleline), Is.True,
                "the server-side elevation check is handed the page's elevation service");
            Assert.That(page, Does.Contain("ElevationSvc.IsElevated(u)"), "a Live ticket still asks for the password first");
            Assert.That(page, Does.Contain("ElevationSvc.VerifyPasswordAsync"), "the password is verified, not just asked for");
            Assert.That(Regex.IsMatch(page, @"\bclient\.PlaceOrderAsync\("), Is.False, "no direct broker call is left");
        });
    }
}
