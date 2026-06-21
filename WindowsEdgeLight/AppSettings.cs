using System;
using System.IO;
using System.Text.Json;

namespace WindowsEdgeLight;

/// <summary>
/// Application settings that persist across sessions
/// </summary>
public class AppSettings
{
    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true
    };

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
    /// Load settings from disk
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, LoadOptions);
                
                // Validate deserialized settings
                if (settings != null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse settings file: {ex.Message}");
            // Delete corrupted settings file
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
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

    public void Normalize()
    {
        Brightness = NormalizeDouble(Brightness, 0.2, 1.0, 1.0);
        ColorTemperature = NormalizeDouble(ColorTemperature, 0.0, 1.0, 0.5);
    }

    /// <summary>
    /// Save settings to disk
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Normalize();
            var json = JsonSerializer.Serialize(this, SaveOptions);
            var tempFilePath = Path.Combine(
                directory ?? Path.GetTempPath(),
                $"{Path.GetFileName(SettingsFilePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(tempFilePath, json);
                File.Move(tempFilePath, SettingsFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static double NormalizeDouble(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
