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
        Assert.That(result, Does.Contain("Strategies"));
    }

    [Test]
    public void GetProjectSettingsFolder_ReturnsPathWithProjectName()
    {
        var result = SettingsManager.GetProjectSettingsFolder("Console");

        Assert.That(result, Does.Contain("Settings"));
        Assert.That(result, Does.Contain("Console"));
    }

    [Test]
    public void GetSettingsFilePath_ReturnsJsonPath()
    {
        var result = SettingsManager.GetSettingsFilePath("Backend");

        Assert.That(result, Does.EndWith("settings.json"));
        Assert.That(result, Does.Contain("Backend"));
    }

    #endregion

    #region Path Consistency

    [Test]
    public void Paths_AreConsistent()
    {
        var baseFolder = SettingsManager.GetBaseFolder();
        var strategiesFolder = SettingsManager.GetStrategiesFolder();

        // Strategies folder should be under base folder
        Assert.That(strategiesFolder, Does.StartWith(baseFolder));
    }

    [Test]
    public void ProjectSettingsFolders_AreSiblings()
    {
        var consoleSettings = SettingsManager.GetProjectSettingsFolder("Console");
        var backendSettings = SettingsManager.GetProjectSettingsFolder("Backend");

        // Both should be under the same parent
        var consoleParent = Path.GetDirectoryName(consoleSettings);
        var backendParent = Path.GetDirectoryName(backendSettings);

        Assert.That(consoleParent, Is.EqualTo(backendParent));
    }

    #endregion
}


