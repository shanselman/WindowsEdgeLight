using System;
using System.IO;
using System.Text.Json;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WindowsEdgeLight.Tests")]

namespace WindowsEdgeLight;

/// <summary>
/// Application settings that persist across sessions
/// </summary>
public class AppSettings
{
    internal static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsEdgeLight",
        "settings.json");

    internal static readonly JsonSerializerOptions LoadOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    internal static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// When enabled, excludes the edge light from screen capture (Teams, screenshots, etc.)
    /// Note: When enabled, screenshots won't capture the edge light effect
    /// </summary>
    public bool ExcludeFromCapture { get; set; } = true;

    /// <summary>
    /// Load settings from disk
    /// </summary>
    public static AppSettings Load() => Load(SettingsFilePath);

    /// <summary>
    /// Load settings from a specific file path (used for testing)
    /// </summary>
    internal static AppSettings Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, LoadOptions);
                if (settings != null)
                    return settings;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse settings file: {ex.Message}");
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
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
    public void Save() => Save(SettingsFilePath);

    /// <summary>
    /// Save settings to a specific file path (used for testing)
    /// </summary>
    internal void Save(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(this, SaveOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
