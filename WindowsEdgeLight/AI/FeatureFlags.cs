using System.IO;
using Windows.Media.FaceAnalysis;

namespace WindowsEdgeLight.AI;

/// <summary>
/// Manages feature flags for AI capabilities, with settings persistence.
/// </summary>
public static class FeatureFlags
{
    private static bool _isInitialized;
    private static bool _isAIFaceTrackingEnabled;
    private static double _aiIntensityMultiplier = 0.7;

    /// <summary>
    /// Whether AI face tracking is available on this machine.
    /// Uses Windows built-in FaceTracker API.
    /// </summary>
    public static bool IsAIFaceTrackingAvailable
    {
        get
        {
            EnsureInitialized();
            return FaceTracker.IsSupported;
        }
    }

    /// <summary>
    /// Whether AI face tracking is enabled by the user.
    /// </summary>
    public static bool IsAIFaceTrackingEnabled
    {
        get
        {
            EnsureInitialized();
            return _isAIFaceTrackingEnabled && IsAIFaceTrackingAvailable;
        }
        set
        {
            _isAIFaceTrackingEnabled = value;
            SaveSettings();
            AIFaceTrackingEnabledChanged?.Invoke(null, value);
        }
    }

    /// <summary>
    /// Intensity multiplier for AI-driven lighting effects (0.0 to 1.0).
    /// </summary>
    public static double AIIntensityMultiplier
    {
        get
        {
            EnsureInitialized();
            return _aiIntensityMultiplier;
        }
        set
        {
            _aiIntensityMultiplier = Math.Clamp(value, 0.0, 1.0);
            SaveSettings();
        }
    }

    /// <summary>
    /// Event fired when AI face tracking is enabled or disabled.
    /// </summary>
    public static event EventHandler<bool>? AIFaceTrackingEnabledChanged;

    /// <summary>
    /// Initialize feature flags (call early in app startup).
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;
        
        LoadSettings();
        _isInitialized = true;
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            Initialize();
        }
    }

    private static string SettingsFilePath => 
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsEdgeLight",
            "ai-settings.json");

    private static void LoadSettings()
    {
        try
        {
            var settingsPath = SettingsFilePath;
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AISettings>(json);
                if (settings != null)
                {
                    _isAIFaceTrackingEnabled = settings.IsAIFaceTrackingEnabled;
                    _aiIntensityMultiplier = settings.AIIntensityMultiplier;
                }
            }
        }
        catch (Exception)
        {
            // Use defaults if settings can't be loaded
        }
    }

    private static void SaveSettings()
    {
        try
        {
            var settingsPath = SettingsFilePath;
            var directory = Path.GetDirectoryName(settingsPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new AISettings
            {
                IsAIFaceTrackingEnabled = _isAIFaceTrackingEnabled,
                AIIntensityMultiplier = _aiIntensityMultiplier
            };

            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(settingsPath, json);
        }
        catch (Exception)
        {
            // Ignore save failures
        }
    }

    private class AISettings
    {
        public bool IsAIFaceTrackingEnabled { get; set; }
        public double AIIntensityMultiplier { get; set; } = 0.7;
    }
}
