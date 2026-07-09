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
        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
        Assert.Equal(0.5, settings.ColorTemperature);
        Assert.True(settings.ShowToggleButton);
        Assert.True(settings.ShowBrightnessButtons);
        Assert.True(settings.ShowColorTempButtons);
        Assert.True(settings.ShowMonitorControlButtons);
    }

    [Fact]
    public void NewInstance_HasExpectedDefaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.ExcludeFromCapture);
        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
        Assert.Equal(0.5, settings.ColorTemperature);
        Assert.True(settings.ShowToggleButton);
        Assert.True(settings.ShowBrightnessButtons);
        Assert.True(settings.ShowColorTempButtons);
        Assert.True(settings.ShowMonitorControlButtons);
    }

    // ── Round-trip: boolean properties ───────────────────────────────────────

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
    public void Save_ThenLoad_RoundTripsIsLightOn_False()
    {
        new AppSettings { IsLightOn = false }.Save();
        Assert.False(AppSettings.Load().IsLightOn);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsIsLightOn_True()
    {
        new AppSettings { IsLightOn = true }.Save();
        Assert.True(AppSettings.Load().IsLightOn);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsShowToggleButton_False()
    {
        new AppSettings { ShowToggleButton = false }.Save();
        Assert.False(AppSettings.Load().ShowToggleButton);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsShowBrightnessButtons_False()
    {
        new AppSettings { ShowBrightnessButtons = false }.Save();
        Assert.False(AppSettings.Load().ShowBrightnessButtons);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsShowColorTempButtons_False()
    {
        new AppSettings { ShowColorTempButtons = false }.Save();
        Assert.False(AppSettings.Load().ShowColorTempButtons);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsShowMonitorControlButtons_False()
    {
        new AppSettings { ShowMonitorControlButtons = false }.Save();
        Assert.False(AppSettings.Load().ShowMonitorControlButtons);
    }

    // ── Round-trip: double properties ─────────────────────────────────────────

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(0.75)]
    public void Save_ThenLoad_RoundTripsBrightness(double value)
    {
        new AppSettings { Brightness = value }.Save();
        Assert.Equal(value, AppSettings.Load().Brightness);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(0.3)]
    public void Save_ThenLoad_RoundTripsColorTemperature(double value)
    {
        new AppSettings { ColorTemperature = value }.Save();
        Assert.Equal(value, AppSettings.Load().ColorTemperature);
    }

    // ── All properties together ───────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTripsAllProperties()
    {
        var original = new AppSettings
        {
            ExcludeFromCapture = false,
            IsLightOn = false,
            Brightness = 0.65,
            ColorTemperature = 0.8,
            ShowToggleButton = false,
            ShowBrightnessButtons = false,
            ShowColorTempButtons = false,
            ShowMonitorControlButtons = false,
        };
        original.Save();

        var loaded = AppSettings.Load();

        Assert.False(loaded.ExcludeFromCapture);
        Assert.False(loaded.IsLightOn);
        Assert.Equal(0.65, loaded.Brightness);
        Assert.Equal(0.8, loaded.ColorTemperature);
        Assert.False(loaded.ShowToggleButton);
        Assert.False(loaded.ShowBrightnessButtons);
        Assert.False(loaded.ShowColorTempButtons);
        Assert.False(loaded.ShowMonitorControlButtons);
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
        new AppSettings().Save();

        var json = File.ReadAllText(_testSettingsPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ── Error recovery ────────────────────────────────────────────────────────

    [Fact]
    public void Load_WithCorruptedJson_ReturnsDefaults()
    {
        File.WriteAllText(_testSettingsPath, "{ this is not valid json!!!");
        var settings = AppSettings.Load();
        Assert.True(settings.ExcludeFromCapture);
        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
    }

    [Fact]
    public void Load_WithCorruptedJson_DeletesCorruptedFile()
    {
        File.WriteAllText(_testSettingsPath, "{ this is not valid json!!!");
        AppSettings.Load();
        Assert.False(File.Exists(_testSettingsPath));
    }

    [Fact]
    public void Load_WithEmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_testSettingsPath, string.Empty);
        var settings = AppSettings.Load();
        Assert.True(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_WithNullJsonValue_ReturnsDefaults()
    {
        File.WriteAllText(_testSettingsPath, "null");
        var settings = AppSettings.Load();
        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Forward compatibility ─────────────────────────────────────────────────

    [Fact]
    public void Load_WithUnknownProperties_IgnoresThemAndReturnsValidSettings()
    {
        var json = """
            {
                "ExcludeFromCapture": false,
                "IsLightOn": false,
                "Brightness": 0.4,
                "ColorTemperature": 0.7,
                "FutureProperty": "someValue",
                "AnotherFutureFlag": true
            }
            """;
        File.WriteAllText(_testSettingsPath, json);

        var settings = AppSettings.Load();

        Assert.False(settings.ExcludeFromCapture);
        Assert.False(settings.IsLightOn);
        Assert.Equal(0.4, settings.Brightness);
        Assert.Equal(0.7, settings.ColorTemperature);
    }

    // ── JSON format tolerance ─────────────────────────────────────────────────

    [Fact]
    public void Load_WithTrailingCommas_ParsesSuccessfully()
    {
        var json = """
            {
                "ExcludeFromCapture": false,
                "IsLightOn": true,
                "Brightness": 0.9,
            }
            """;
        File.WriteAllText(_testSettingsPath, json);

        var settings = AppSettings.Load();
        Assert.False(settings.ExcludeFromCapture);
        Assert.Equal(0.9, settings.Brightness);
    }

    [Fact]
    public void Load_WithJsonComments_ParsesSuccessfully()
    {
        var json = """
            {
                // This is a comment
                "ExcludeFromCapture": true,
                /* Block comment */
                "IsLightOn": false,
                "Brightness": 0.6,
                "ColorTemperature": 0.2
            }
            """;
        File.WriteAllText(_testSettingsPath, json);

        var settings = AppSettings.Load();
        Assert.True(settings.ExcludeFromCapture);
        Assert.False(settings.IsLightOn);
        Assert.Equal(0.6, settings.Brightness);
        Assert.Equal(0.2, settings.ColorTemperature);
    }

    // ── Consistency / idempotency ─────────────────────────────────────────────

    [Fact]
    public void MultipleLoad_AfterSave_ReturnsConsistentValues()
    {
        new AppSettings { IsLightOn = false, Brightness = 0.45 }.Save();

        var first = AppSettings.Load();
        var second = AppSettings.Load();

        Assert.Equal(first.IsLightOn, second.IsLightOn);
        Assert.Equal(first.Brightness, second.Brightness);
    }

    [Fact]
    public void Save_CalledTwice_OverwritesWithLatestValue()
    {
        new AppSettings { Brightness = 0.3 }.Save();
        new AppSettings { Brightness = 0.9 }.Save();

        Assert.Equal(0.9, AppSettings.Load().Brightness);
    }

    [Fact]
    public void Save_ThenModify_ThenLoadAgain_ReturnsSavedNotModifiedValue()
    {
        var s = new AppSettings { ColorTemperature = 0.7 };
        s.Save();
        s.ColorTemperature = 0.1; // Modify in memory, don't save

        Assert.Equal(0.7, AppSettings.Load().ColorTemperature);
    }
}
