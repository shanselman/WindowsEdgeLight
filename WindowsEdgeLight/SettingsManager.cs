using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WindowsEdgeLight;

public static class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsEdgeLight");
    
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly string TempFilePath = Path.Combine(SettingsDirectory, "settings.tmp");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly SemaphoreSlim SaveLock = new(1, 1);
    private static CancellationTokenSource? _debounceCts;

    public static UserSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Debug.WriteLine($"Settings file not found, using defaults: {SettingsFilePath}");
                return UserSettings.Default;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            
            if (settings == null)
            {
                Debug.WriteLine("Failed to deserialize settings, using defaults");
                return UserSettings.Default;
            }

            settings.Validate();
            Debug.WriteLine($"Settings loaded successfully from: {SettingsFilePath}");
            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading settings: {ex.Message}");
            return UserSettings.Default;
        }
    }

    public static async Task SaveSettingsAsync(UserSettings settings, bool debounce = false)
    {
        if (debounce)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            try
            {
                await Task.Delay(500, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        await SaveLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            
            settings.Validate();
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            
            await File.WriteAllTextAsync(TempFilePath, json);
            
            File.Move(TempFilePath, SettingsFilePath, overwrite: true);
            
            Debug.WriteLine($"Settings saved successfully to: {SettingsFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
        finally
        {
            SaveLock.Release();
        }
    }

    public static void ResetSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Delete(SettingsFilePath);
                Debug.WriteLine("Settings file deleted");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting settings: {ex.Message}");
        }
    }
}
