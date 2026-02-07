// ============================================================================
// SettingsManagerTests - Tests for settings management
// ============================================================================
//
// Base folder is the solution root directory.
//
// ============================================================================

using IdiotProof.Core.Settings;

namespace IdiotProof.Core.UnitTests.Settings;

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

    #endregion

}


