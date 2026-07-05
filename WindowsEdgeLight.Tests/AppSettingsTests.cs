using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsEdgeLight.Tests;

/// <summary>
/// All AppSettings tests run serially to safely override the static SettingsFilePath.
/// </summary>
[Collection("AppSettings")]
public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testSettingsPath;
    private readonly string _originalPath;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WEL_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _testSettingsPath = Path.Combine(_tempDir, "settings.json");

        _originalPath = AppSettings.SettingsFilePath;
        AppSettings.SettingsFilePath = _testSettingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsFilePath = _originalPath;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void Load_WhenNoFileExists_ReturnsDefaultSettings()
    {
        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    [Fact]
    public void NewInstance_HasExpectedDefaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTripsExcludeFromCapture_True()
    {
        new AppSettings { ExcludeFromCapture = true }.Save();

        Assert.True(AppSettings.Load().ExcludeFromCapture);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsExcludeFromCapture_False()
    {
        new AppSettings { ExcludeFromCapture = false }.Save();

        Assert.False(AppSettings.Load().ExcludeFromCapture);
    }

    [Fact]
    public void MultipleLoad_AfterSave_ReturnsConsistentValues()
    {
        new AppSettings { ExcludeFromCapture = false }.Save();

        Assert.Equal(AppSettings.Load().ExcludeFromCapture, AppSettings.Load().ExcludeFromCapture);
    }

    // ── File creation ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_CreatesSettingsFile()
    {
        new AppSettings().Save();

        Assert.True(File.Exists(_testSettingsPath));
    }

    [Fact]
    public void Save_ProducesValidJson()
    {
        new AppSettings { ExcludeFromCapture = false }.Save();

        var json = File.ReadAllText(_testSettingsPath);
        var doc  = JsonDocument.Parse(json);

        Assert.NotNull(doc);
    }

    [Fact]
    public void Save_JsonContainsExcludeFromCaptureProperty()
    {
        new AppSettings { ExcludeFromCapture = true }.Save();

        var json = File.ReadAllText(_testSettingsPath);

        Assert.Contains("ExcludeFromCapture", json);
    }

    // ── Overwrite ─────────────────────────────────────────────────────────────

    [Fact]
    public void Save_OverwritesPreviousFile()
    {
        new AppSettings { ExcludeFromCapture = true  }.Save();
        new AppSettings { ExcludeFromCapture = false }.Save();

        Assert.False(AppSettings.Load().ExcludeFromCapture);
    }

    // ── Error recovery ────────────────────────────────────────────────────────

    [Fact]
    public void Load_WithCorruptedJson_ReturnsDefaults()
    {
        File.WriteAllText(_testSettingsPath, "{ this is not valid json !!! }");

        var loaded = AppSettings.Load();

        Assert.NotNull(loaded);
        Assert.True(loaded.ExcludeFromCapture);
    }

    [Fact]
    public void Load_WithCorruptedJson_DeletesCorruptedFile()
    {
        File.WriteAllText(_testSettingsPath, "{ bad json }");

        AppSettings.Load();

        Assert.False(File.Exists(_testSettingsPath), "Corrupted settings file should be deleted");
    }

    [Fact]
    public void Load_WithEmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_testSettingsPath, string.Empty);

        var loaded = AppSettings.Load();

        Assert.NotNull(loaded);
    }

    [Fact]
    public void Load_WithNullJson_ReturnsDefaults()
    {
        File.WriteAllText(_testSettingsPath, "null");

        var loaded = AppSettings.Load();

        Assert.NotNull(loaded);
        Assert.True(loaded.ExcludeFromCapture);
    }

    // ── Forward-compatibility (unknown properties) ────────────────────────────

    [Fact]
    public void Load_WithUnknownProperties_IgnoresThemAndLoadsKnownOnes()
    {
        File.WriteAllText(_testSettingsPath, """
            {
                "ExcludeFromCapture": false,
                "FutureProperty": "some value",
                "AnotherUnknown": 42
            }
            """);

        Assert.False(AppSettings.Load().ExcludeFromCapture);
    }

    // ── JSON format features ──────────────────────────────────────────────────

    [Fact]
    public void Load_WithTrailingComma_ParsesSuccessfully()
    {
        File.WriteAllText(_testSettingsPath, """
            {
                "ExcludeFromCapture": false,
            }
            """);

        Assert.False(AppSettings.Load().ExcludeFromCapture);
    }

    [Fact]
    public void Load_WithJsonComments_ParsesSuccessfully()
    {
        File.WriteAllText(_testSettingsPath, """
            {
                // This is a comment
                "ExcludeFromCapture": false
            }
            """);

        Assert.False(AppSettings.Load().ExcludeFromCapture);
    }

    // ── Property mutation ─────────────────────────────────────────────────────

    [Fact]
    public void ExcludeFromCapture_CanBeSetAndRetrieved()
    {
        var settings = new AppSettings();

        settings.ExcludeFromCapture = false;
        Assert.False(settings.ExcludeFromCapture);

        settings.ExcludeFromCapture = true;
        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public void SaveTwice_SameValues_ProducesIdenticalFiles()
    {
        var settings = new AppSettings { ExcludeFromCapture = true };
        settings.Save();
        var first = File.ReadAllText(_testSettingsPath);

        settings.Save();
        var second = File.ReadAllText(_testSettingsPath);

        Assert.Equal(first, second);
    }

    // ── Static path properties ────────────────────────────────────────────────

    [Fact]
    public void DefaultSettingsFilePath_IsUnderApplicationDataFolder()
    {
        Assert.Contains("WindowsEdgeLight", AppSettings.DefaultSettingsFilePath);
        Assert.Contains("settings.json",    AppSettings.DefaultSettingsFilePath);
    }
}
