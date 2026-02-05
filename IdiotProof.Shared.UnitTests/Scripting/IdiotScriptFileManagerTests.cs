// ============================================================================
// IdiotScriptFileManagerTests - Tests for .idiot file management
// ============================================================================

using IdiotProof.Shared.Scripting;

namespace IdiotProof.Shared.UnitTests.Scripting;

/// <summary>
/// Tests for IdiotScriptFileManager class.
/// </summary>
[TestFixture]
public class IdiotScriptFileManagerTests
{
    #region Constants

    [Test]
    public void FileExtension_EqualsIdiot()
    {
        Assert.That(IdiotScriptFileManager.FileExtension, Is.EqualTo("idiot"));
    }

    [Test]
    public void FileExtensionWithDot_EqualsIdiotWithDot()
    {
        Assert.That(IdiotScriptFileManager.FileExtensionWithDot, Is.EqualTo(".idiot"));
    }

    [Test]
    public void SearchPattern_EqualsWildcardIdiot()
    {
        Assert.That(IdiotScriptFileManager.SearchPattern, Is.EqualTo("*.idiot"));
    }

    #endregion

    #region Folder Paths

    [Test]
    public void GetBaseFolder_ReturnsNonEmptyPath()
    {
        var result = IdiotScriptFileManager.GetBaseFolder();

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GetDefaultFolder_ReturnsNonEmptyPath()
    {
        var result = IdiotScriptFileManager.GetDefaultFolder();

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GetSettingsFolder_ReturnsPathWithProjectName()
    {
        var result = IdiotScriptFileManager.GetSettingsFolder("TestProject");

        Assert.That(result, Does.Contain("TestProject"));
    }

    [Test]
    public void GetDateFolder_ReturnsPathWithDate()
    {
        var date = new DateOnly(2025, 6, 15);
        var result = IdiotScriptFileManager.GetDateFolder(date);

        Assert.That(result, Does.Contain("2025-06-15"));
    }

    [Test]
    public void GetDateFolder_WithBaseFolder_UsesProvidedBase()
    {
        var date = new DateOnly(2025, 6, 15);
        var customBase = @"C:\Custom\Path";
        var result = IdiotScriptFileManager.GetDateFolder(date, customBase);

        Assert.That(result, Does.StartWith(customBase));
        Assert.That(result, Does.Contain("2025-06-15"));
    }

    #endregion

    #region Path Consistency

    [Test]
    public void DefaultFolder_IsUnderBaseFolder()
    {
        var baseFolder = IdiotScriptFileManager.GetBaseFolder();
        var defaultFolder = IdiotScriptFileManager.GetDefaultFolder();

        Assert.That(defaultFolder, Does.StartWith(baseFolder));
    }

    [Test]
    public void DateFolder_IsUnderDefaultFolder()
    {
        var defaultFolder = IdiotScriptFileManager.GetDefaultFolder();
        var dateFolder = IdiotScriptFileManager.GetDateFolder(DateOnly.FromDateTime(DateTime.Now));

        Assert.That(dateFolder, Does.StartWith(defaultFolder));
    }

    #endregion
}


