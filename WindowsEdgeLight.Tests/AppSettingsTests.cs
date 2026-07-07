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

        var first = AppSettings.Load().ExcludeFromCapture;
        var second = AppSettings.Load().ExcludeFromCapture;

        Assert.Equal(first, second);
    }

    // ── File creation ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_CreatesFileAtConfiguredPath()
    {
        new AppSettings().Save();

        Assert.True(File.Exists(_testSettingsPath));
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        var deepDir = Path.Combine(_tempDir, "a", "b", "c");
        var deepPath = Path.Combine(deepDir, "settings.json");
        AppSettings.SettingsFilePath = deepPath;

        new AppSettings().Save();

        Assert.True(File.Exists(deepPath));
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        new AppSettings { ExcludeFromCapture = true }.Save();

        var json = File.ReadAllText(_testSettingsPath);
        // Should not throw
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ── Error recovery ────────────────────────────────────────────────────────

    [Fact]
    public void Load_WithCorruptedJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath, "{ invalid json !!!!");

        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture); // default
    }

    [Fact]
    public void Load_WithCorruptedJson_DeletesCorruptedFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath, "not json at all");

        AppSettings.Load();

        Assert.False(File.Exists(_testSettingsPath));
    }

    [Fact]
    public void Load_WithEmptyFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath, string.Empty);

        var settings = AppSettings.Load();

        Assert.NotNull(settings);
    }

    [Fact]
    public void Load_WithNullJsonValue_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath, "null");

        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Forward-compatibility ─────────────────────────────────────────────────

    [Fact]
    public void Load_WithUnknownProperties_IgnoresThemAndReturnsValidSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath,
            """{"ExcludeFromCapture": false, "FutureProperty": 42}""");

        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.False(settings.ExcludeFromCapture);
    }

    // ── JSON format features ──────────────────────────────────────────────────

    [Fact]
    public void Load_WithTrailingCommas_ParsesSuccessfully()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath,
            """{"ExcludeFromCapture": true,}""");

        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_WithJsonComments_ParsesSuccessfully()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_testSettingsPath)!);
        File.WriteAllText(_testSettingsPath,
            """
            {
              // This is a comment
              "ExcludeFromCapture": false
            }
            """);

        var settings = AppSettings.Load();

        Assert.NotNull(settings);
        Assert.False(settings.ExcludeFromCapture);
    }

    // ── Property mutation ─────────────────────────────────────────────────────

    [Fact]
    public void ExcludeFromCapture_CanBeToggled()
    {
        var settings = new AppSettings { ExcludeFromCapture = true };
        settings.ExcludeFromCapture = false;

        Assert.False(settings.ExcludeFromCapture);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public void Save_CalledTwice_OverwritesWithLatestValue()
    {
        new AppSettings { ExcludeFromCapture = true }.Save();
        new AppSettings { ExcludeFromCapture = false }.Save();

        Assert.False(AppSettings.Load().ExcludeFromCapture);
    }

    [Fact]
    public void Save_ThenModify_ThenLoadAgain_ReturnsSavedNotModifiedValue()
    {
        var original = new AppSettings { ExcludeFromCapture = true };
        original.Save();

        // Mutate in-memory (do not save again)
        original.ExcludeFromCapture = false;

        // Load from disk — should still reflect what was saved
        var loaded = AppSettings.Load();
        Assert.True(loaded.ExcludeFromCapture);
    }
}
