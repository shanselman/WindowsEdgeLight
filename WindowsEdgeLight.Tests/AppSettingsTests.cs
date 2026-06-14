using System;
using System.IO;
using System.Text.Json;

namespace WindowsEdgeLight.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _settingsPath;

    public AppSettingsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"WindowsEdgeLightTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _settingsPath = Path.Combine(_testDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── Load ───────────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture); // default is true
    }

    [Fact]
    public void Load_ValidFile_ReturnsPersistedValue()
    {
        File.WriteAllText(_settingsPath, """{"ExcludeFromCapture": false}""");

        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.False(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_ValidFile_TrailingCommaAllowed()
    {
        // JSON with trailing comma should not throw
        File.WriteAllText(_settingsPath, """{"ExcludeFromCapture": true,}""");

        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_ValidFile_CommentsAllowed()
    {
        File.WriteAllText(_settingsPath, """
            // This is a comment
            {"ExcludeFromCapture": false}
            """);

        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.False(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndDeletesFile()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json !!!");

        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture); // default
        Assert.False(File.Exists(_settingsPath));  // corrupted file deleted
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "");

        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_NullJsonValue_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "null");

        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Save ───────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesJsonFile()
    {
        var settings = new AppSettings { ExcludeFromCapture = false };

        settings.SaveTo(_settingsPath);

        Assert.True(File.Exists(_settingsPath));
        var json = File.ReadAllText(_settingsPath);
        Assert.Contains("ExcludeFromCapture", json);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_testDir, "sub", "dir", "settings.json");

        var settings = new AppSettings();
        settings.SaveTo(nestedPath);

        Assert.True(File.Exists(nestedPath));
    }

    // ── Round-trip ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ExcludeFromCapture_PreservesValue(bool value)
    {
        var original = new AppSettings { ExcludeFromCapture = value };
        original.SaveTo(_settingsPath);

        var loaded = AppSettings.LoadFrom(_settingsPath);

        Assert.Equal(value, loaded.ExcludeFromCapture);
    }

    [Fact]
    public void RoundTrip_Defaults_PreservesDefaults()
    {
        var original = new AppSettings();
        original.SaveTo(_settingsPath);

        var loaded = AppSettings.LoadFrom(_settingsPath);

        Assert.Equal(original.ExcludeFromCapture, loaded.ExcludeFromCapture);
    }
}
