using System;
using System.IO;
using System.Text.Json;

namespace WindowsEdgeLight.Tests;

public class AppSettingsTests : IDisposable
{
    // Isolated temp directory per test instance to avoid cross-test pollution.
    private readonly string _testDir;
    private readonly string _settingsPath;

    public AppSettingsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_testDir);
        _settingsPath = Path.Combine(_testDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // ---- Default values ----

    [Fact]
    public void NewInstance_ExcludeFromCapture_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.ExcludeFromCapture);
    }

    // ---- Load: missing file returns defaults ----

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var settings = AppSettings.Load(_settingsPath);
        Assert.True(settings.ExcludeFromCapture);
    }

    // ---- Round-trip Save/Load ----

    [Fact]
    public void SaveAndLoad_ExcludeFromCapture_False_RoundTrips()
    {
        var original = new AppSettings { ExcludeFromCapture = false };
        original.Save(_settingsPath);

        var loaded = AppSettings.Load(_settingsPath);
        Assert.False(loaded.ExcludeFromCapture);
    }

    [Fact]
    public void SaveAndLoad_ExcludeFromCapture_True_RoundTrips()
    {
        var original = new AppSettings { ExcludeFromCapture = true };
        original.Save(_settingsPath);

        var loaded = AppSettings.Load(_settingsPath);
        Assert.True(loaded.ExcludeFromCapture);
    }

    // ---- Save creates directory if absent ----

    [Fact]
    public void Save_CreatesDirectoryIfAbsent()
    {
        var nestedPath = Path.Combine(_testDir, "nested", "subdir", "settings.json");

        var settings = new AppSettings();
        settings.Save(nestedPath);

        Assert.True(File.Exists(nestedPath));
    }

    // ---- Save produces valid JSON ----

    [Fact]
    public void Save_ProducesValidJsonFile()
    {
        var settings = new AppSettings { ExcludeFromCapture = false };
        settings.Save(_settingsPath);

        Assert.True(File.Exists(_settingsPath));

        var json = File.ReadAllText(_settingsPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ---- Recovery from corrupt JSON ----

    [Fact]
    public void Load_WithCorruptJson_ReturnsDefaultsAndDeletesFile()
    {
        File.WriteAllText(_settingsPath, "{ this is not valid json !!!");

        var loaded = AppSettings.Load(_settingsPath);

        Assert.True(loaded.ExcludeFromCapture);   // defaults restored
        Assert.False(File.Exists(_settingsPath));  // corrupted file deleted
    }

    // ---- JSON with trailing commas is accepted ----

    [Fact]
    public void Load_JsonWithTrailingComma_IsAccepted()
    {
        File.WriteAllText(_settingsPath, "{ \"ExcludeFromCapture\": false, }");

        var loaded = AppSettings.Load(_settingsPath);
        Assert.False(loaded.ExcludeFromCapture);
    }

    // ---- JSON with C-style comments is accepted ----

    [Fact]
    public void Load_JsonWithComments_IsAccepted()
    {
        File.WriteAllText(_settingsPath, "{ /* comment */ \"ExcludeFromCapture\": true }");

        var loaded = AppSettings.Load(_settingsPath);
        Assert.True(loaded.ExcludeFromCapture);
    }

    // ---- Unknown properties are ignored gracefully ----

    [Fact]
    public void Load_JsonWithExtraProperties_DoesNotThrow()
    {
        File.WriteAllText(_settingsPath, "{ \"ExcludeFromCapture\": true, \"FutureProperty\": 42 }");

        var ex = Record.Exception(() => AppSettings.Load(_settingsPath));
        Assert.Null(ex);
    }

    // ---- Multiple save/load cycles remain stable ----

    [Fact]
    public void MultipleRoundTrips_SettingsRemainStable()
    {
        for (int i = 0; i < 5; i++)
        {
            var s = new AppSettings { ExcludeFromCapture = i % 2 == 0 };
            s.Save(_settingsPath);
            var loaded = AppSettings.Load(_settingsPath);
            Assert.Equal(s.ExcludeFromCapture, loaded.ExcludeFromCapture);
        }
    }
}
