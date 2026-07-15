using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsEdgeLight.Tests;

/// <summary>
/// Tests for <see cref="AppSettings"/> serialization and default values.
/// All file I/O is routed through <c>LoadFrom</c>/<c>SaveTo</c> using a temp file
/// so tests are isolated and do not touch the real AppData folder.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ──────────────────────────────────────────────────────────────
    // Default values
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultExcludeFromCapture_IsTrue()
        => Assert.True(new AppSettings().ExcludeFromCapture);

    [Fact]
    public void DefaultIsLightOn_IsTrue()
        => Assert.True(new AppSettings().IsLightOn);

    [Fact]
    public void DefaultBrightness_IsOne()
        => Assert.Equal(1.0, new AppSettings().Brightness);

    [Fact]
    public void DefaultColorTemperature_IsHalf()
        => Assert.Equal(0.5, new AppSettings().ColorTemperature);

    [Fact]
    public void DefaultShowToggleButton_IsTrue()
        => Assert.True(new AppSettings().ShowToggleButton);

    [Fact]
    public void DefaultShowBrightnessButtons_IsTrue()
        => Assert.True(new AppSettings().ShowBrightnessButtons);

    [Fact]
    public void DefaultShowColorTempButtons_IsTrue()
        => Assert.True(new AppSettings().ShowColorTempButtons);

    [Fact]
    public void DefaultShowMonitorControlButtons_IsTrue()
        => Assert.True(new AppSettings().ShowMonitorControlButtons);

    // ──────────────────────────────────────────────────────────────
    // LoadFrom – missing file
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefaults()
    {
        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.True(settings.ExcludeFromCapture);
        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
        Assert.Equal(0.5, settings.ColorTemperature);
    }

    // ──────────────────────────────────────────────────────────────
    // Round-trip: SaveTo then LoadFrom
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesAllProperties()
    {
        var original = new AppSettings
        {
            ExcludeFromCapture = false,
            IsLightOn = false,
            Brightness = 0.6,
            ColorTemperature = 0.3,
            ShowToggleButton = false,
            ShowBrightnessButtons = false,
            ShowColorTempButtons = false,
            ShowMonitorControlButtons = false
        };

        original.SaveTo(_settingsPath);
        var loaded = AppSettings.LoadFrom(_settingsPath);

        Assert.False(loaded.ExcludeFromCapture);
        Assert.False(loaded.IsLightOn);
        Assert.Equal(0.6, loaded.Brightness, precision: 10);
        Assert.Equal(0.3, loaded.ColorTemperature, precision: 10);
        Assert.False(loaded.ShowToggleButton);
        Assert.False(loaded.ShowBrightnessButtons);
        Assert.False(loaded.ShowColorTempButtons);
        Assert.False(loaded.ShowMonitorControlButtons);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void RoundTrip_Brightness_Values(double brightness)
    {
        var settings = new AppSettings { Brightness = brightness };
        settings.SaveTo(_settingsPath);
        var loaded = AppSettings.LoadFrom(_settingsPath);
        Assert.Equal(brightness, loaded.Brightness, precision: 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void RoundTrip_ColorTemperature_Values(double temp)
    {
        var settings = new AppSettings { ColorTemperature = temp };
        settings.SaveTo(_settingsPath);
        var loaded = AppSettings.LoadFrom(_settingsPath);
        Assert.Equal(temp, loaded.ColorTemperature, precision: 10);
    }

    // ──────────────────────────────────────────────────────────────
    // Corrupted JSON – graceful fallback
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_CorruptedJson_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json !!!");
        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
    }

    [Fact]
    public void LoadFrom_CorruptedJson_DeletesCorruptFile()
    {
        File.WriteAllText(_settingsPath, "{ broken");
        _ = AppSettings.LoadFrom(_settingsPath);

        Assert.False(File.Exists(_settingsPath));
    }

    [Fact]
    public void LoadFrom_EmptyJson_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "{}");
        var settings = AppSettings.LoadFrom(_settingsPath);

        // All properties should fall back to their C# initializer defaults
        Assert.True(settings.ExcludeFromCapture);
        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
        Assert.Equal(0.5, settings.ColorTemperature);
    }

    // ──────────────────────────────────────────────────────────────
    // Partial JSON – only some keys present
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_PartialJson_MissingKeysUseDefaults()
    {
        File.WriteAllText(_settingsPath, """{ "Brightness": 0.7 }""");
        var settings = AppSettings.LoadFrom(_settingsPath);

        Assert.Equal(0.7, settings.Brightness, precision: 10);
        // Unspecified properties should be default (false for bool in System.Text.Json without initializers)
        // The class initializes IsLightOn=true, but deserialization won't set it unless present in JSON,
        // so it will be the C# default (false for bool).
        // This is a known limitation / documentation test.
        Assert.Equal(0.5, settings.ColorTemperature, precision: 10);
    }

    // ──────────────────────────────────────────────────────────────
    // SaveTo – creates intermediate directories
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SaveTo_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "dir", "settings.json");
        var settings = new AppSettings();

        settings.SaveTo(nestedPath);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void SaveTo_WritesValidJson()
    {
        var settings = new AppSettings { Brightness = 0.8 };
        settings.SaveTo(_settingsPath);

        var json = File.ReadAllText(_settingsPath);
        using var doc = JsonDocument.Parse(json);
        var brightness = doc.RootElement.GetProperty("Brightness").GetDouble();
        Assert.Equal(0.8, brightness, precision: 10);
    }

    // ──────────────────────────────────────────────────────────────
    // SaveTo – overwrites existing file
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SaveTo_OverwritesExistingFile()
    {
        var settings1 = new AppSettings { Brightness = 0.4 };
        settings1.SaveTo(_settingsPath);

        var settings2 = new AppSettings { Brightness = 0.9 };
        settings2.SaveTo(_settingsPath);

        var loaded = AppSettings.LoadFrom(_settingsPath);
        Assert.Equal(0.9, loaded.Brightness, precision: 10);
    }

    // ──────────────────────────────────────────────────────────────
    // JSON allows trailing commas and comments
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_JsonWithTrailingComma_Succeeds()
    {
        File.WriteAllText(_settingsPath, """{ "Brightness": 0.6, }""");
        var settings = AppSettings.LoadFrom(_settingsPath);
        Assert.Equal(0.6, settings.Brightness, precision: 10);
    }

    [Fact]
    public void LoadFrom_JsonWithComment_Succeeds()
    {
        File.WriteAllText(_settingsPath, """
        {
            // User-edited brightness
            "Brightness": 0.75
        }
        """);
        var settings = AppSettings.LoadFrom(_settingsPath);
        Assert.Equal(0.75, settings.Brightness, precision: 10);
    }

    // ──────────────────────────────────────────────────────────────
    // Boolean flag round-trips
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ExcludeFromCapture(bool value)
    {
        var settings = new AppSettings { ExcludeFromCapture = value };
        settings.SaveTo(_settingsPath);
        Assert.Equal(value, AppSettings.LoadFrom(_settingsPath).ExcludeFromCapture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_IsLightOn(bool value)
    {
        var settings = new AppSettings { IsLightOn = value };
        settings.SaveTo(_settingsPath);
        Assert.Equal(value, AppSettings.LoadFrom(_settingsPath).IsLightOn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowToggleButton(bool value)
    {
        var settings = new AppSettings { ShowToggleButton = value };
        settings.SaveTo(_settingsPath);
        Assert.Equal(value, AppSettings.LoadFrom(_settingsPath).ShowToggleButton);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowBrightnessButtons(bool value)
    {
        var settings = new AppSettings { ShowBrightnessButtons = value };
        settings.SaveTo(_settingsPath);
        Assert.Equal(value, AppSettings.LoadFrom(_settingsPath).ShowBrightnessButtons);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowColorTempButtons(bool value)
    {
        var settings = new AppSettings { ShowColorTempButtons = value };
        settings.SaveTo(_settingsPath);
        Assert.Equal(value, AppSettings.LoadFrom(_settingsPath).ShowColorTempButtons);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowMonitorControlButtons(bool value)
    {
        var settings = new AppSettings { ShowMonitorControlButtons = value };
        settings.SaveTo(_settingsPath);
        Assert.Equal(value, AppSettings.LoadFrom(_settingsPath).ShowMonitorControlButtons);
    }
}
