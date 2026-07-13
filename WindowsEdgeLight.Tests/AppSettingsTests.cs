using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsEdgeLight.Tests;

/// <summary>
/// Tests for AppSettings serialisation, default values, validation, and round-trips.
/// The test project includes AppSettings.cs directly (no net10.0-windows build required).
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _path;

    public AppSettingsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"WELTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _path = Path.Combine(_testDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Default values ─────────────────────────────────────────────────────

    [Fact]
    public void Defaults_ExcludeFromCapture_IsTrue()
        => Assert.True(new AppSettings().ExcludeFromCapture);

    [Fact]
    public void Defaults_IsLightOn_IsTrue()
        => Assert.True(new AppSettings().IsLightOn);

    [Fact]
    public void Defaults_Brightness_IsOne()
        => Assert.Equal(1.0, new AppSettings().Brightness);

    [Fact]
    public void Defaults_ColorTemperature_IsHalf()
        => Assert.Equal(0.5, new AppSettings().ColorTemperature);

    [Fact]
    public void Defaults_ShowToggleButton_IsTrue()
        => Assert.True(new AppSettings().ShowToggleButton);

    [Fact]
    public void Defaults_ShowBrightnessButtons_IsTrue()
        => Assert.True(new AppSettings().ShowBrightnessButtons);

    [Fact]
    public void Defaults_ShowColorTempButtons_IsTrue()
        => Assert.True(new AppSettings().ShowColorTempButtons);

    [Fact]
    public void Defaults_ShowMonitorControlButtons_IsTrue()
        => Assert.True(new AppSettings().ShowMonitorControlButtons);

    // ── Load: missing / corrupt ────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = AppSettings.LoadFrom(_path);
        Assert.True(s.ExcludeFromCapture);
        Assert.True(s.IsLightOn);
        Assert.Equal(1.0, s.Brightness);
        Assert.Equal(0.5, s.ColorTemperature);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndDeletesFile()
    {
        File.WriteAllText(_path, "{ not valid json !!!");
        var s = AppSettings.LoadFrom(_path);
        Assert.True(s.ExcludeFromCapture);   // defaults
        Assert.False(File.Exists(_path));    // file deleted
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_path, "");
        var s = AppSettings.LoadFrom(_path);
        Assert.True(s.ExcludeFromCapture);
    }

    [Fact]
    public void Load_NullJsonValue_ReturnsDefaults()
    {
        File.WriteAllText(_path, "null");
        var s = AppSettings.LoadFrom(_path);
        Assert.True(s.ExcludeFromCapture);
    }

    // ── Load: permissive JSON ──────────────────────────────────────────────

    [Fact]
    public void Load_TrailingComma_IsAccepted()
    {
        File.WriteAllText(_path, """{"ExcludeFromCapture": false,}""");
        var s = AppSettings.LoadFrom(_path);
        Assert.False(s.ExcludeFromCapture);
    }

    [Fact]
    public void Load_LineComment_IsAccepted()
    {
        File.WriteAllText(_path, """
            // persisted settings
            {"IsLightOn": false}
            """);
        var s = AppSettings.LoadFrom(_path);
        Assert.False(s.IsLightOn);
    }

    // ── Load: values for each property ────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_ExcludeFromCapture_PersistedValue(bool value)
    {
        File.WriteAllText(_path, $$"""{"ExcludeFromCapture": {{value.ToString().ToLower()}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).ExcludeFromCapture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_IsLightOn_PersistedValue(bool value)
    {
        File.WriteAllText(_path, $$"""{"IsLightOn": {{value.ToString().ToLower()}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).IsLightOn);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Load_Brightness_PersistedValue(double value)
    {
        File.WriteAllText(_path, $$"""{"Brightness": {{value}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).Brightness, precision: 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Load_ColorTemperature_PersistedValue(double value)
    {
        File.WriteAllText(_path, $$"""{"ColorTemperature": {{value}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).ColorTemperature, precision: 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_ShowToggleButton_PersistedValue(bool value)
    {
        File.WriteAllText(_path, $$"""{"ShowToggleButton": {{value.ToString().ToLower()}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).ShowToggleButton);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_ShowBrightnessButtons_PersistedValue(bool value)
    {
        File.WriteAllText(_path, $$"""{"ShowBrightnessButtons": {{value.ToString().ToLower()}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).ShowBrightnessButtons);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_ShowColorTempButtons_PersistedValue(bool value)
    {
        File.WriteAllText(_path, $$"""{"ShowColorTempButtons": {{value.ToString().ToLower()}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).ShowColorTempButtons);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_ShowMonitorControlButtons_PersistedValue(bool value)
    {
        File.WriteAllText(_path, $$"""{"ShowMonitorControlButtons": {{value.ToString().ToLower()}}}""");
        Assert.Equal(value, AppSettings.LoadFrom(_path).ShowMonitorControlButtons);
    }

    // ── Validate: clamp out-of-range values ───────────────────────────────

    [Fact]
    public void Validate_BrightnessAboveMax_ClampedToOne()
    {
        var s = new AppSettings { Brightness = 2.5 }.Validate();
        Assert.Equal(1.0, s.Brightness);
    }

    [Fact]
    public void Validate_BrightnessBelowMin_ClampedToPointTwo()
    {
        var s = new AppSettings { Brightness = -0.5 }.Validate();
        Assert.Equal(0.2, s.Brightness);
    }

    [Fact]
    public void Validate_BrightnessAtMin_Unchanged()
    {
        var s = new AppSettings { Brightness = 0.2 }.Validate();
        Assert.Equal(0.2, s.Brightness, precision: 10);
    }

    [Fact]
    public void Validate_ColorTempAboveMax_ClampedToOne()
    {
        var s = new AppSettings { ColorTemperature = 1.5 }.Validate();
        Assert.Equal(1.0, s.ColorTemperature);
    }

    [Fact]
    public void Validate_ColorTempBelowMin_ClampedToZero()
    {
        var s = new AppSettings { ColorTemperature = -0.1 }.Validate();
        Assert.Equal(0.0, s.ColorTemperature);
    }

    [Fact]
    public void Load_OutOfRangeBrightness_IsClampedOnLoad()
    {
        File.WriteAllText(_path, """{"Brightness": 99.9}""");
        var s = AppSettings.LoadFrom(_path);
        Assert.Equal(1.0, s.Brightness);
    }

    [Fact]
    public void Load_NegativeBrightness_IsClampedOnLoad()
    {
        File.WriteAllText(_path, """{"Brightness": -5.0}""");
        var s = AppSettings.LoadFrom(_path);
        Assert.Equal(0.2, s.Brightness);
    }

    [Fact]
    public void Load_OutOfRangeColorTemp_IsClampedOnLoad()
    {
        File.WriteAllText(_path, """{"ColorTemperature": 3.14}""");
        var s = AppSettings.LoadFrom(_path);
        Assert.Equal(1.0, s.ColorTemperature);
    }

    // ── Save ──────────────────────────────────────────────────────────────

    [Fact]
    public void Save_CreatesFile()
    {
        new AppSettings().SaveTo(_path);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        new AppSettings().SaveTo(_path);
        var json = File.ReadAllText(_path);
        // should not throw
        _ = JsonDocument.Parse(json);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_testDir, "sub", "dir", "settings.json");
        new AppSettings().SaveTo(nested);
        Assert.True(File.Exists(nested));
    }

    // ── Round-trips ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ExcludeFromCapture(bool v)
    {
        new AppSettings { ExcludeFromCapture = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).ExcludeFromCapture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_IsLightOn(bool v)
    {
        new AppSettings { IsLightOn = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).IsLightOn);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void RoundTrip_Brightness(double v)
    {
        new AppSettings { Brightness = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).Brightness, precision: 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void RoundTrip_ColorTemperature(double v)
    {
        new AppSettings { ColorTemperature = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).ColorTemperature, precision: 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowToggleButton(bool v)
    {
        new AppSettings { ShowToggleButton = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).ShowToggleButton);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowBrightnessButtons(bool v)
    {
        new AppSettings { ShowBrightnessButtons = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).ShowBrightnessButtons);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowColorTempButtons(bool v)
    {
        new AppSettings { ShowColorTempButtons = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).ShowColorTempButtons);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_ShowMonitorControlButtons(bool v)
    {
        new AppSettings { ShowMonitorControlButtons = v }.SaveTo(_path);
        Assert.Equal(v, AppSettings.LoadFrom(_path).ShowMonitorControlButtons);
    }

    [Fact]
    public void RoundTrip_AllProperties_Preserved()
    {
        var original = new AppSettings
        {
            ExcludeFromCapture = false,
            IsLightOn = false,
            Brightness = 0.7,
            ColorTemperature = 0.3,
            ShowToggleButton = false,
            ShowBrightnessButtons = false,
            ShowColorTempButtons = false,
            ShowMonitorControlButtons = false,
        };
        original.SaveTo(_path);
        var loaded = AppSettings.LoadFrom(_path);

        Assert.Equal(original.ExcludeFromCapture, loaded.ExcludeFromCapture);
        Assert.Equal(original.IsLightOn, loaded.IsLightOn);
        Assert.Equal(original.Brightness, loaded.Brightness, precision: 10);
        Assert.Equal(original.ColorTemperature, loaded.ColorTemperature, precision: 10);
        Assert.Equal(original.ShowToggleButton, loaded.ShowToggleButton);
        Assert.Equal(original.ShowBrightnessButtons, loaded.ShowBrightnessButtons);
        Assert.Equal(original.ShowColorTempButtons, loaded.ShowColorTempButtons);
        Assert.Equal(original.ShowMonitorControlButtons, loaded.ShowMonitorControlButtons);
    }
}
