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
    public bool ExcludeFromCapture { get; set; } = false;

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

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
