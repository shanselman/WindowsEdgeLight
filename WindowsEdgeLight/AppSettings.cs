using System;
using System.IO;
using System.Text.Json;

namespace WindowsEdgeLight;

/// <summary>
/// Application settings that persist across sessions
/// </summary>
public class AppSettings
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsEdgeLight",
        "settings.json");

    /// <summary>
    /// When enabled, excludes the edge light from screen capture (Teams, screenshots, etc.)
    /// Note: When enabled, screenshots won't capture the edge light effect
    /// </summary>
    public bool ExcludeFromCapture { get; set; } = true;

    /// <summary>
    /// Whether the edge light is on or off (persisted across restarts)
    /// </summary>
    public bool IsLightOn { get; set; } = true;

    /// <summary>
    /// Brightness/opacity of the edge light, in the range [0.2, 1.0]
    /// </summary>
    public double Brightness { get; set; } = 1.0;

    /// <summary>
    /// Color temperature of the edge light, in the range [0.0, 1.0]
    /// where 0.0 = coolest (blue-white) and 1.0 = warmest (amber)
    /// </summary>
    public double ColorTemperature { get; set; } = 0.5;

    /// <summary>
    /// Whether to show toggle button in the control window
    /// </summary>
    public bool ShowToggleButton { get; set; } = true;

    /// <summary>
    /// Whether to show brightness buttons in the control window
    /// </summary>
    public bool ShowBrightnessButtons { get; set; } = true;

    /// <summary>
    /// Whether to show color temperature buttons in the control window
    /// </summary>
    public bool ShowColorTempButtons { get; set; } = true;

    /// <summary>
    /// Whether to show window control buttons (toggle, switch monitor, all monitors) in the control window
    /// </summary>
    public bool ShowMonitorControlButtons { get; set; } = true;

    /// <summary>
    /// Clamp numeric settings to their valid ranges, returning this instance.
    /// Called automatically after loading so corrupted/hand-edited files cannot
    /// put the application into an invalid state.
    /// </summary>
    public AppSettings Validate()
    {
        Brightness = Math.Max(0.2, Math.Min(1.0, Brightness));
        ColorTemperature = Math.Max(0.0, Math.Min(1.0, ColorTemperature));
        return this;
    }

    /// <summary>
    /// Load settings from disk
    /// </summary>
    public static AppSettings Load() => LoadFrom(SettingsFilePath);

    /// <summary>
    /// Load settings from the specified file path (overload used by tests).
    /// </summary>
    internal static AppSettings LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

                if (settings != null)
                {
                    return settings.Validate();
                }
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse settings file: {ex.Message}");
            // Delete corrupted settings file
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { /* Ignore deletion errors */ }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>
    /// Save settings to disk
    /// </summary>
    public void Save() => SaveTo(SettingsFilePath);

    /// <summary>
    /// Save settings to the specified file path (overload used by tests).
    /// </summary>
    internal void SaveTo(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
