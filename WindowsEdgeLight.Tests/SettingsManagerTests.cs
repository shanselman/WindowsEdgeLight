using System.IO;
using System.Text.Json;
using FluentAssertions;
using WindowsEdgeLight;

namespace WindowsEdgeLight.Tests;

public class SettingsManagerTests : IDisposable
{
    private readonly string _originalSettingsPath;
    private readonly string _testDirectory;

    public SettingsManagerTests()
    {
        // Create a unique test directory for each test run
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"WindowsEdgeLightTests_{Guid.NewGuid()}");
        
        Directory.CreateDirectory(_testDirectory);

        // Override settings path using environment variable if possible
        // For now, tests will use real path but we'll clean up manually
        _originalSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsEdgeLight",
            "settings.json");
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Fact]
    public void LoadSettings_ReturnsDefaults_WhenFileDoesNotExist()
    {
        // Ensure file doesn't exist
        if (File.Exists(_originalSettingsPath))
        {
            File.Delete(_originalSettingsPath);
        }

        var settings = SettingsManager.LoadSettings();

        settings.Should().NotBeNull();
        settings.IsLightOn.Should().BeTrue();
        settings.Brightness.Should().Be(1.0);
        settings.ColorTemperature.Should().Be(0.5);
    }

    [Fact]
    public void LoadSettings_ReturnsDefaults_WhenFileContainsInvalidJson()
    {
        var settingsDir = Path.GetDirectoryName(_originalSettingsPath)!;
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(_originalSettingsPath, "{ invalid json ,,, }");

        var settings = SettingsManager.LoadSettings();

        settings.Should().NotBeNull();
        settings.Should().BeEquivalentTo(UserSettings.Default);
    }

    [Fact]
    public void LoadSettings_ReturnsDefaults_WhenFileIsEmpty()
    {
        var settingsDir = Path.GetDirectoryName(_originalSettingsPath)!;
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(_originalSettingsPath, "");

        var settings = SettingsManager.LoadSettings();

        settings.Should().NotBeNull();
        settings.Should().BeEquivalentTo(UserSettings.Default);
    }

    [Fact]
    public async Task SaveSettingsAsync_CreatesDirectoryIfMissing()
    {
        // Clean up first
        if (Directory.Exists(Path.GetDirectoryName(_originalSettingsPath)))
        {
            Directory.Delete(Path.GetDirectoryName(_originalSettingsPath)!, recursive: true);
        }

        var settings = new UserSettings { Brightness = 0.8 };
        await SettingsManager.SaveSettingsAsync(settings);

        Directory.Exists(Path.GetDirectoryName(_originalSettingsPath)).Should().BeTrue();
        File.Exists(_originalSettingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveSettingsAsync_WritesValidJson()
    {
        var settings = new UserSettings
        {
            IsLightOn = false,
            Brightness = 0.7,
            ColorTemperature = 0.3,
            CurrentMonitorIndex = 2,
            ShowOnAllMonitors = true,
            IsControlWindowVisible = false
        };

        await SettingsManager.SaveSettingsAsync(settings);

        File.Exists(_originalSettingsPath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(_originalSettingsPath);
        json.Should().Contain("\"IsLightOn\": false");
        json.Should().Contain("\"Brightness\": 0.7");
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesAllValues()
    {
        var original = new UserSettings
        {
            Version = "1.0",
            IsLightOn = false,
            Brightness = 0.75,
            ColorTemperature = 0.3,
            CurrentMonitorIndex = 2,
            ShowOnAllMonitors = true,
            IsControlWindowVisible = false
        };

        await SettingsManager.SaveSettingsAsync(original);
        var loaded = SettingsManager.LoadSettings();

        loaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task SaveSettingsAsync_ValidatesBeforeSaving()
    {
        var settings = new UserSettings
        {
            Brightness = 5.0,  // Invalid - should be clamped to 1.0
            ColorTemperature = -1.0  // Invalid - should be clamped to 0.0
        };

        await SettingsManager.SaveSettingsAsync(settings);
        var loaded = SettingsManager.LoadSettings();

        loaded.Brightness.Should().Be(1.0);
        loaded.ColorTemperature.Should().Be(0.0);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithDebounce_DelaysExecution()
    {
        var settings = new UserSettings { Brightness = 0.5 };
        var startTime = DateTime.UtcNow;

        await SettingsManager.SaveSettingsAsync(settings, debounce: true);

        var elapsed = DateTime.UtcNow - startTime;
        elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(450); // Allow some margin
    }

    [Fact]
    public async Task SaveSettingsAsync_WithoutDebounce_ExecutesImmediately()
    {
        var settings = new UserSettings { Brightness = 0.5 };
        var startTime = DateTime.UtcNow;

        await SettingsManager.SaveSettingsAsync(settings, debounce: false);

        var elapsed = DateTime.UtcNow - startTime;
        elapsed.TotalMilliseconds.Should().BeLessThan(200);
    }

    [Fact]
    public void ResetSettings_DeletesFile_WhenFileExists()
    {
        // Ensure file exists
        var settingsDir = Path.GetDirectoryName(_originalSettingsPath)!;
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(_originalSettingsPath, "{}");

        SettingsManager.ResetSettings();

        File.Exists(_originalSettingsPath).Should().BeFalse();
    }

    [Fact]
    public void ResetSettings_DoesNotThrow_WhenFileDoesNotExist()
    {
        // Ensure file doesn't exist
        if (File.Exists(_originalSettingsPath))
        {
            File.Delete(_originalSettingsPath);
        }

        var action = () => SettingsManager.ResetSettings();

        action.Should().NotThrow();
    }

    [Fact]
    public async Task SaveSettingsAsync_OverwritesExistingFile()
    {
        var firstSettings = new UserSettings { Brightness = 0.5 };
        await SettingsManager.SaveSettingsAsync(firstSettings);

        var secondSettings = new UserSettings { Brightness = 0.9 };
        await SettingsManager.SaveSettingsAsync(secondSettings);

        var loaded = SettingsManager.LoadSettings();
        loaded.Brightness.Should().Be(0.9);
    }

    [Fact]
    public void LoadSettings_ValidatesAfterLoad()
    {
        // Manually write invalid settings to file
        var settingsDir = Path.GetDirectoryName(_originalSettingsPath)!;
        Directory.CreateDirectory(settingsDir);
        
        var invalidJson = @"{
            ""brightness"": 10.0,
            ""colorTemperature"": -5.0,
            ""currentMonitorIndex"": -3
        }";
        File.WriteAllText(_originalSettingsPath, invalidJson);

        var loaded = SettingsManager.LoadSettings();

        loaded.Brightness.Should().Be(1.0);
        loaded.ColorTemperature.Should().Be(0.0);
        loaded.CurrentMonitorIndex.Should().Be(0);
    }
}
