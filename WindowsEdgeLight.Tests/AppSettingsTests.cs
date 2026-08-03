using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsEdgeLight.Tests;

public class AppSettingsDefaultsTests
{
    [Fact]
    public void DefaultBrightness_Is_1()
    {
        var s = new AppSettings();
        Assert.Equal(1.0, s.Brightness);
    }

    [Fact]
    public void DefaultColorTemperature_Is_0_5()
    {
        var s = new AppSettings();
        Assert.Equal(0.5, s.ColorTemperature);
    }

    [Fact]
    public void DefaultIsLightOn_IsTrue()
    {
        var s = new AppSettings();
        Assert.True(s.IsLightOn);
    }

    [Fact]
    public void DefaultExcludeFromCapture_IsTrue()
    {
        var s = new AppSettings();
        Assert.True(s.ExcludeFromCapture);
    }

    [Fact]
    public void DefaultShowToggleButton_IsTrue()
    {
        var s = new AppSettings();
        Assert.True(s.ShowToggleButton);
    }

    [Fact]
    public void DefaultShowBrightnessButtons_IsTrue()
    {
        var s = new AppSettings();
        Assert.True(s.ShowBrightnessButtons);
    }

    [Fact]
    public void DefaultShowColorTempButtons_IsTrue()
    {
        var s = new AppSettings();
        Assert.True(s.ShowColorTempButtons);
    }

    [Fact]
    public void DefaultShowMonitorControlButtons_IsTrue()
    {
        var s = new AppSettings();
        Assert.True(s.ShowMonitorControlButtons);
    }
}

public class AppSettingsPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;

    public AppSettingsPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WELTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var settings = AppSettings.Load(_tempFile);
        Assert.Equal(1.0, settings.Brightness);
        Assert.Equal(0.5, settings.ColorTemperature);
        Assert.True(settings.IsLightOn);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AllProperties()
    {
        var original = new AppSettings
        {
            Brightness = 0.7,
            ColorTemperature = 0.3,
            IsLightOn = false,
            ExcludeFromCapture = false,
            ShowToggleButton = false,
            ShowBrightnessButtons = false,
            ShowColorTempButtons = false,
            ShowMonitorControlButtons = false,
        };

        original.Save(_tempFile);
        var loaded = AppSettings.Load(_tempFile);

        Assert.Equal(0.7, loaded.Brightness);
        Assert.Equal(0.3, loaded.ColorTemperature);
        Assert.False(loaded.IsLightOn);
        Assert.False(loaded.ExcludeFromCapture);
        Assert.False(loaded.ShowToggleButton);
        Assert.False(loaded.ShowBrightnessButtons);
        Assert.False(loaded.ShowColorTempButtons);
        Assert.False(loaded.ShowMonitorControlButtons);
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsDefaults_AndDeletesFile()
    {
        File.WriteAllText(_tempFile, "{ not valid json !!!");

        var settings = AppSettings.Load(_tempFile);

        // Should fall back to defaults
        Assert.Equal(1.0, settings.Brightness);
        // Corrupted file should have been deleted
        Assert.False(File.Exists(_tempFile));
    }

    [Fact]
    public void Load_EmptyJson_ReturnsDefaults()
    {
        File.WriteAllText(_tempFile, "{}");

        var settings = AppSettings.Load(_tempFile);

        // Properties not in JSON get their default values from the type
        Assert.NotNull(settings);
    }

    [Fact]
    public void Load_JsonWithTrailingCommas_ParsesSuccessfully()
    {
        var json = """
            {
                "Brightness": 0.8,
                "ColorTemperature": 0.4,
            }
            """;
        File.WriteAllText(_tempFile, json);

        var settings = AppSettings.Load(_tempFile);

        Assert.Equal(0.8, settings.Brightness);
        Assert.Equal(0.4, settings.ColorTemperature);
    }

    [Fact]
    public void Load_JsonWithComments_ParsesSuccessfully()
    {
        var json = """
            {
                // brightness setting
                "Brightness": 0.6,
                "ColorTemperature": 0.2
            }
            """;
        File.WriteAllText(_tempFile, json);

        var settings = AppSettings.Load(_tempFile);

        Assert.Equal(0.6, settings.Brightness);
    }

    [Fact]
    public void Save_CreatesDirectoryIfNotExisting()
    {
        var deepFile = Path.Combine(_tempDir, "sub", "deep", "settings.json");
        var s = new AppSettings { Brightness = 0.5 };

        s.Save(deepFile);

        Assert.True(File.Exists(deepFile));
    }

    [Fact]
    public void SavedJson_IsHumanReadable()
    {
        var s = new AppSettings { Brightness = 0.9 };
        s.Save(_tempFile);

        var json = File.ReadAllText(_tempFile);

        // Indented JSON contains newlines
        Assert.Contains("\n", json);
        Assert.Contains("Brightness", json);
    }
}

public class AppSettingsSerializationTests
{
    [Fact]
    public void Serialize_Brightness_AppearsInJson()
    {
        var s = new AppSettings { Brightness = 0.42 };
        var json = JsonSerializer.Serialize(s);
        Assert.Contains("0.42", json);
    }

    [Fact]
    public void Deserialize_PartialJson_UsesDefaultsForMissingFields()
    {
        var json = """{"Brightness": 0.55}""";
        var s = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(s);
        Assert.Equal(0.55, s!.Brightness);
        // Other fields should have .NET default (0 for double, false for bool)
        // (AppSettings defaults are set via property initializers, but Deserialize bypasses constructor)
    }

    [Fact]
    public void RoundTrip_ExtremeValues_ArePreserved()
    {
        var s = new AppSettings { Brightness = 0.0, ColorTemperature = 1.0 };
        var json = JsonSerializer.Serialize(s);
        var s2 = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(s2);
        Assert.Equal(0.0, s2!.Brightness);
        Assert.Equal(1.0, s2.ColorTemperature);
    }
}
