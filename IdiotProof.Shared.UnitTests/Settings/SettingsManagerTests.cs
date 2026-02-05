// ============================================================================
// SettingsManagerTests - Tests for settings management
// ============================================================================
//
// Base folder is the solution root directory.
//
// ============================================================================

using IdiotProof.Shared.Settings;

namespace IdiotProof.Shared.UnitTests.Settings;

/// <summary>
/// Tests for SettingsManager class.
/// </summary>
[TestFixture]
public class SettingsManagerTests
{
    [SetUp]
    public void SetUp()
    {
        SettingsManager.ResetBaseFolder();
    }

    [TearDown]
    public void TearDown()
    {
        SettingsManager.ResetBaseFolder();
    }

    #region Path Helpers

    [Test]
    public void GetBaseFolder_ReturnsSolutionRootDirectory()
    {
        var result = SettingsManager.GetBaseFolder();

        Assert.That(result, Is.Not.Null.And.Not.Empty);
        Assert.That(Path.IsPathRooted(result), Is.True);
    }

    [Test]
    public void SetBaseFolder_OverridesDefault()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "TestIdiotProof");
        SettingsManager.SetBaseFolder(customPath);

        var result = SettingsManager.GetBaseFolder();

        Assert.That(result, Is.EqualTo(customPath));
    }

    [Test]
    public void GetStrategiesFolder_ReturnsPathInBaseFolder()
    {
        var result = SettingsManager.GetStrategiesFolder();
        var baseFolder = SettingsManager.GetBaseFolder();

        Assert.That(result, Does.StartWith(baseFolder));
        Assert.That(result, Does.Contain("IdiotProof.Scripts"));
    }

    #endregion

}


