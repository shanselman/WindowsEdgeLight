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
    /// Load settings from disk
    /// </summary>
    /// <param name="settingsFilePath">Optional override path; defaults to the standard AppData location.</param>
    public static AppSettings Load(string? settingsFilePath = null)
    {
        var path = settingsFilePath ?? SettingsFilePath;
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
                
                // Validate deserialized settings
                if (settings != null)
                {
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
    /// <param name="settingsFilePath">Optional override path; defaults to the standard AppData location.</param>
    public void Save(string? settingsFilePath = null)
    {
        var path = settingsFilePath ?? SettingsFilePath;
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
