using System.Text.Json;
using Xunit;

namespace WindowsEdgeLight.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"WindowsEdgeLight.Tests-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(tempDirectory, "settings.json");

    [Fact]
    public void DefaultsMatchApplicationDefaults()
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

    [Fact]
    public void MissingFileReturnsDefaults()
    {
        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(1.0, settings.Brightness);
        Assert.True(settings.IsLightOn);
    }

    [Fact]
    public void SaveAndLoadRoundTripsAllProperties()
    {
        var expected = new AppSettings
        {
            ExcludeFromCapture = false,
            IsLightOn = false,
            Brightness = 0.7,
            ColorTemperature = 0.3,
            ShowToggleButton = false,
            ShowBrightnessButtons = false,
            ShowColorTempButtons = false,
            ShowMonitorControlButtons = false
        };

        expected.SaveTo(SettingsPath);
        var actual = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(expected.ExcludeFromCapture, actual.ExcludeFromCapture);
        Assert.Equal(expected.IsLightOn, actual.IsLightOn);
        Assert.Equal(expected.Brightness, actual.Brightness);
        Assert.Equal(expected.ColorTemperature, actual.ColorTemperature);
        Assert.Equal(expected.ShowToggleButton, actual.ShowToggleButton);
        Assert.Equal(expected.ShowBrightnessButtons, actual.ShowBrightnessButtons);
        Assert.Equal(expected.ShowColorTempButtons, actual.ShowColorTempButtons);
        Assert.Equal(expected.ShowMonitorControlButtons, actual.ShowMonitorControlButtons);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void SaveReplacesExistingFile()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, """{"Brightness":0.2}""");
        var settings = new AppSettings { Brightness = 0.8 };

        settings.SaveTo(SettingsPath);

        Assert.Equal(0.8, AppSettings.LoadFrom(SettingsPath).Brightness);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void SaveCreatesParentDirectory()
    {
        var nestedPath = Path.Combine(tempDirectory, "nested", "settings.json");

        new AppSettings().SaveTo(nestedPath);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void CorruptJsonReturnsDefaultsAndDeletesFile()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, "{ invalid");

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(1.0, settings.Brightness);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, """
            {
              // User-edited settings remain supported.
              "Brightness": 0.6,
              "ColorTemperature": 0.2,
            }
            """);

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(0.6, settings.Brightness);
        Assert.Equal(0.2, settings.ColorTemperature);
    }

    [Fact]
    public void SavedJsonIsIndented()
    {
        new AppSettings().SaveTo(SettingsPath);

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains(Environment.NewLine, json);
        Assert.NotNull(JsonSerializer.Deserialize<AppSettings>(json));
    }

    [Fact]
    public void JsonNullLiteralReturnsDefaultsAndDeletesFile()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, "null");

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(1.0, settings.Brightness);
        Assert.True(settings.IsLightOn);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void EmptyJsonObjectReturnsDefaults()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, "{}");

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.True(settings.ExcludeFromCapture);
        Assert.True(settings.IsLightOn);
        Assert.Equal(1.0, settings.Brightness);
        Assert.Equal(0.5, settings.ColorTemperature);
    }

    [Fact]
    public void PartialJsonRestoresSpecifiedAndDefaultsForOthers()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, """{"Brightness":0.4,"IsLightOn":false}""");

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(0.4, settings.Brightness);
        Assert.False(settings.IsLightOn);
        // Unspecified properties use defaults
        Assert.True(settings.ExcludeFromCapture);
        Assert.Equal(0.5, settings.ColorTemperature);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
