using IdiotProof.Operations.Hosting;

namespace IdiotProof.Operations.Tests.Hosting;

/// <summary>IP-T76: as a Windows service the Hub roots itself at its exe folder and keeps a window log.</summary>
public class HubHostingTests
{
    [Test]
    public void ContentRoot_IsTheExeFolderOnlyAsAService()
    {
        Assert.That(HubHosting.ContentRoot(true, @"C:\Apps\IdiotProof\Hub\"), Is.EqualTo(@"C:\Apps\IdiotProof\Hub\"));
        Assert.That(HubHosting.ContentRoot(false, @"C:\Apps\IdiotProof\Hub\"), Is.Null);
    }

    [Test]
    public void WindowLog_ExplicitWins_ServiceDefaultsUnderTheUsersLocalAppData()
    {
        Assert.That(HubHosting.WindowLogPath(true, @"D:\logs\hub.log", @"C:\Users\me\AppData\Local"), Is.EqualTo(@"D:\logs\hub.log"));
        Assert.That(HubHosting.WindowLogPath(true, null, @"C:\Users\me\AppData\Local"),
            Is.EqualTo(Path.Combine(@"C:\Users\me\AppData\Local", "MindAttic", "IdiotProof", "Logs", "hub-service.log")));
        Assert.That(HubHosting.WindowLogPath(false, null, @"C:\Users\me\AppData\Local"), Is.Null);
    }
}
