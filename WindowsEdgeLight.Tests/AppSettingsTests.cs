using System;
using System.IO;
using System.Text.Json;
using WindowsEdgeLight;

namespace WindowsEdgeLight.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFile;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsFile = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Default values ──────────────────────────────────────────────

    [Fact]
    public void DefaultSettings_ExcludeFromCapture_IsTrue()
    {
        var settings = new AppSettings();
        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Load when no file exists ────────────────────────────────────

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var settings = AppSettings.Load(_settingsFile);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    // ── Save / Load round-trip ──────────────────────────────────────

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesExcludeFromCapture()
    {
        var original = new AppSettings { ExcludeFromCapture = false };
        original.Save(_settingsFile);

        var loaded = AppSettings.Load(_settingsFile);

        Assert.False(loaded.ExcludeFromCapture);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_ExcludeFromCapture_True()
    {
        var original = new AppSettings { ExcludeFromCapture = true };
        original.Save(_settingsFile);

        var loaded = AppSettings.Load(_settingsFile);

        Assert.True(loaded.ExcludeFromCapture);
    }

    [Fact]
    public void Save_CreatesFileOnDisk()
    {
        var settings = new AppSettings();
        settings.Save(_settingsFile);

        Assert.True(File.Exists(_settingsFile));
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        var settings = new AppSettings { ExcludeFromCapture = false };
        settings.Save(_settingsFile);

        var json = File.ReadAllText(_settingsFile);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ExcludeFromCapture", out var prop));
        Assert.Equal(JsonValueKind.False, prop.ValueKind);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var deepFile = Path.Combine(_tempDir, "subdir", "nested", "settings.json");

        var settings = new AppSettings();
        settings.Save(deepFile);

        Assert.True(File.Exists(deepFile));
    }

    // ── Corrupted / invalid JSON ────────────────────────────────────

    [Fact]
    public void Load_CorruptedJson_ReturnsDefaults()
    {
        File.WriteAllText(_settingsFile, "{ this is not valid json !!!");

        var settings = AppSettings.Load(_settingsFile);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture); // default
    }

    [Fact]
    public void Load_CorruptedJson_DeletesCorruptFile()
    {
        File.WriteAllText(_settingsFile, "<<< corrupt >>>");

        AppSettings.Load(_settingsFile);

        Assert.False(File.Exists(_settingsFile));
    }

    [Fact]
    public void Load_EmptyJson_ReturnsDefaults()
    {
        File.WriteAllText(_settingsFile, "{}");

        var settings = AppSettings.Load(_settingsFile);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_JsonWithTrailingComma_Succeeds()
    {
        File.WriteAllText(_settingsFile, """{"ExcludeFromCapture":false,}""");

        var settings = AppSettings.Load(_settingsFile);

        Assert.False(settings.ExcludeFromCapture);
    }

    [Fact]
    public void Load_JsonWithComment_Succeeds()
    {
        File.WriteAllText(_settingsFile, """
            {
              // this is a comment
              "ExcludeFromCapture": false
            }
            """);

        var settings = AppSettings.Load(_settingsFile);

        Assert.False(settings.ExcludeFromCapture);
    }

    // ── Overwrite behaviour ─────────────────────────────────────────

    [Fact]
    public void Save_Overwrite_KeepsLatestValue()
    {
        var first = new AppSettings { ExcludeFromCapture = false };
        first.Save(_settingsFile);

        var second = new AppSettings { ExcludeFromCapture = true };
        second.Save(_settingsFile);

        var loaded = AppSettings.Load(_settingsFile);
        Assert.True(loaded.ExcludeFromCapture);
    }

    [Fact]
    public void Load_AfterExternalFileEdit_ReflectsChanges()
    {
        var original = new AppSettings { ExcludeFromCapture = false };
        original.Save(_settingsFile);

        // Simulate another process writing the file
        File.WriteAllText(_settingsFile, """{"ExcludeFromCapture":true}""");

        var loaded = AppSettings.Load(_settingsFile);
        Assert.True(loaded.ExcludeFromCapture);
    }

    // ── Unknown / extra properties ──────────────────────────────────

    [Fact]
    public void Load_UnknownPropertiesInJson_AreIgnored()
    {
        File.WriteAllText(_settingsFile, """
            {
              "ExcludeFromCapture": false,
              "UnknownFutureProperty": 42,
              "AnotherUnknown": "hello"
            }
            """);

        var settings = AppSettings.Load(_settingsFile);

        Assert.NotNull(settings);
        Assert.False(settings.ExcludeFromCapture);
    }

    // ── Null values in JSON ─────────────────────────────────────────

    [Fact]
    public void Load_NullBooleanValue_ReturnsDefaults()
    {
        // Null for a non-nullable bool causes a JsonException → defaults returned
        File.WriteAllText(_settingsFile, """{"ExcludeFromCapture":null}""");

        var settings = AppSettings.Load(_settingsFile);

        Assert.NotNull(settings);
        Assert.True(settings.ExcludeFromCapture); // falls back to default
    }

    // ── Output format ───────────────────────────────────────────────

    [Fact]
    public void Save_WritesIndentedJson_MultipleLines()
    {
        var settings = new AppSettings();
        settings.Save(_settingsFile);

        var json = File.ReadAllText(_settingsFile);
        var lines = json.Split('\n');
        Assert.True(lines.Length > 1, "Expected indented JSON with multiple lines");
    }

    // ── Default path ────────────────────────────────────────────────

    [Fact]
    public void SettingsFilePath_IsAbsolutePath()
    {
        Assert.True(Path.IsPathRooted(AppSettings.SettingsFilePath));
    }

    [Fact]
    public void SettingsFilePath_EndsWithSettingsJson()
    {
        Assert.EndsWith("settings.json", AppSettings.SettingsFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Serializer options ──────────────────────────────────────────

    [Fact]
    public void LoadOptions_AllowTrailingCommas_IsTrue()
    {
        Assert.True(AppSettings.LoadOptions.AllowTrailingCommas);
    }

    [Fact]
    public void LoadOptions_ReadCommentHandling_IsSkip()
    {
        Assert.Equal(JsonCommentHandling.Skip, AppSettings.LoadOptions.ReadCommentHandling);
    }

    [Fact]
    public void SaveOptions_WriteIndented_IsTrue()
    {
        Assert.True(AppSettings.SaveOptions.WriteIndented);
    }
}
