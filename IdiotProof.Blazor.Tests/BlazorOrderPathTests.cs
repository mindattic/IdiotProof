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
    /// IP-T281: no page, component or service in the web app (IdiotProof.Blazor, IdiotProof.UI)
    /// places, replaces or cancels at a broker itself — every manual order goes through the
    /// gateway (today: the Options ticket via OptionsTradingService.PlaceOrderAsync).
    /// </summary>
    [Test]
    public void WebApp_NeverCallsABrokerDirectly()
    {
        var root = RepoRoot();
        var direct = new Regex(@"(?<receiver>\w+)\s*\.\s*(PlaceOrderAsync|ReplaceOrderAsync|CancelOrderAsync|CancelAllOrdersAsync)\s*\(");
        var offenders = new List<string>();
        foreach (var project in new[] { "IdiotProof.Blazor", "IdiotProof.UI" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*") || line.StartsWith("@*")) continue;
                foreach (Match m in direct.Matches(lines[i]))
                    if (m.Groups["receiver"].Value != "OptionsSvc")
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.That(offenders, Is.Empty, "Route the order through TransactionGateway (OptionsTradingService.PlaceOrderAsync / SubmitElevatedAsync).");
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
