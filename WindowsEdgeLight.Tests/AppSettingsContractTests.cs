using System.Text.Json;
using Xunit;

namespace WindowsEdgeLight.Tests;

/// <summary>
/// Serialization contract tests: verify that the JSON property names written to disk
/// remain stable across refactors. Breaking these names would corrupt existing
/// users' settings files on upgrade.
/// </summary>
public sealed class AppSettingsContractTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"WindowsEdgeLight.ContractTests-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(tempDirectory, "settings.json");

    [Fact]
    public void SavedJsonContainsExpectedPascalCasePropertyNames()
    {
        new AppSettings().SaveTo(SettingsPath);

        var json = File.ReadAllText(SettingsPath);

        // All property names must remain PascalCase so existing settings files
        // continue to load correctly after a rename or refactor.
        Assert.Contains("\"ExcludeFromCapture\"", json);
        Assert.Contains("\"IsLightOn\"", json);
        Assert.Contains("\"Brightness\"", json);
        Assert.Contains("\"ColorTemperature\"", json);
        Assert.Contains("\"ShowToggleButton\"", json);
        Assert.Contains("\"ShowBrightnessButtons\"", json);
        Assert.Contains("\"ShowColorTempButtons\"", json);
        Assert.Contains("\"ShowMonitorControlButtons\"", json);
    }

    [Fact]
    public void PropertyNamesAreCaseSensitiveOnLoad()
    {
        // Document current behaviour: System.Text.Json uses case-sensitive matching
        // by default. A hand-edited file with lower-case keys such as "brightness"
        // will NOT populate the Brightness property; that field silently stays at
        // its default value (1.0).
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, """{"brightness":0.3,"isLightOn":false}""");

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(1.0, settings.Brightness);   // lowercase key was ignored
        Assert.True(settings.IsLightOn);           // lowercase key was ignored
    }

    [Fact]
    public void LoadFromWhenPathIsADirectoryReturnsDefaults()
    {
        // If the path points to a directory (unlikely but defensive) LoadFrom
        // must catch the resulting IOException and return safe defaults rather
        // than propagating the exception to callers.
        Directory.CreateDirectory(SettingsPath); // make path a directory, not a file

        var settings = AppSettings.LoadFrom(SettingsPath);

        Assert.Equal(1.0, settings.Brightness);
        Assert.True(settings.IsLightOn);
    }

    [Fact]
    public void SavedJsonFieldCountMatchesPropertyCount()
    {
        new AppSettings().SaveTo(SettingsPath);

        var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        var fieldCount = doc.RootElement.EnumerateObject().Count();

        // Every public property in AppSettings must appear in the saved JSON.
        // If this count changes, update the serialization contract tests above.
        Assert.Equal(8, fieldCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
