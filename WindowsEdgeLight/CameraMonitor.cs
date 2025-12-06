using Microsoft.Win32;
using System.Security;

namespace WindowsEdgeLight;

/// <summary>
/// Monitors Windows registry to detect camera usage by applications.
/// Uses the CapabilityAccessManager registry keys that Windows uses to track camera access.
/// </summary>
public class CameraMonitor : IDisposable
{
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private RegistryKey? _registryKey;
    private bool _isMonitoring;
    private System.Threading.Timer? _pollingTimer;
    private HashSet<string> _previousActiveApps = new();
    private readonly object _lockObject = new();

    public event EventHandler<CameraStatusChangedEventArgs>? CameraStatusChanged;

    /// <summary>
    /// Gets whether the camera is currently being used by any application.
    /// </summary>
    public bool IsCameraInUse { get; private set; }

    /// <summary>
    /// Starts monitoring camera usage.
    /// </summary>
    public void StartMonitoring()
    {
        if (_isMonitoring) return;

        try
        {
            _registryKey = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            if (_registryKey == null)
            {
                // Registry key doesn't exist - camera access may not be configured
                return;
            }

            _isMonitoring = true;

            // Since there's no direct event-based API for registry changes in this context,
            // we'll use polling with a reasonable interval (2 seconds)
            _pollingTimer = new System.Threading.Timer(CheckCameraStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
        catch (SecurityException)
        {
            // No permission to access registry key
        }
        catch (Exception)
        {
            // Other errors - fail gracefully
        }
    }

    /// <summary>
    /// Stops monitoring camera usage.
    /// </summary>
    public void StopMonitoring()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _registryKey?.Close();
        _registryKey = null;
    }

    private void CheckCameraStatus(object? state)
    {
        if (!_isMonitoring || _registryKey == null) return;

        lock (_lockObject)
        {
            try
            {
                var currentActiveApps = new HashSet<string>();
                var subKeyNames = _registryKey.GetSubKeyNames();

                foreach (var appName in subKeyNames)
                {
                    // Skip NonPackaged apps initially to see what we get
                    // We'll check each app's LastUsedTimeStop value
                    try
                    {
                        using var appKey = _registryKey.OpenSubKey(appName);
                        if (appKey == null) continue;

                        // Check LastUsedTimeStop - if it's 0, the app is currently using the camera
                        // If it's non-zero, it's the timestamp when the app stopped using the camera
                        var lastUsedTimeStop = appKey.GetValue("LastUsedTimeStop");
                        
                        if (lastUsedTimeStop is long stopTime && stopTime == 0)
                        {
                            // Camera is currently in use by this app
                            // Filter out Windows Hello by checking app name
                            if (!IsWindowsHello(appName))
                            {
                                currentActiveApps.Add(appName);
                            }
                        }
                    }
                    catch
                    {
                        // Skip apps we can't read
                        continue;
                    }
                }

                // Check if status changed
                bool wasInUse = IsCameraInUse;
                bool isNowInUse = currentActiveApps.Count > 0;

                if (wasInUse != isNowInUse)
                {
                    IsCameraInUse = isNowInUse;
                    _previousActiveApps = currentActiveApps;

                    // Raise event on the thread pool thread
                    CameraStatusChanged?.Invoke(this, new CameraStatusChangedEventArgs(isNowInUse, currentActiveApps.ToList()));
                }
                else if (isNowInUse && !_previousActiveApps.SetEquals(currentActiveApps))
                {
                    // Apps changed but camera still in use
                    _previousActiveApps = currentActiveApps;
                }
            }
            catch
            {
                // Fail silently - don't disrupt the app
            }
        }
    }

    private bool IsWindowsHello(string appName)
    {
        // Windows Hello typically uses specific package names
        // Common patterns: Microsoft.Windows.Hello, Windows.Internal, etc.
        var lowerAppName = appName.ToLowerInvariant();
        return lowerAppName.Contains("hello") || 
               lowerAppName.Contains("windows.internal") ||
               lowerAppName.Contains("microsoft.windows.cloudexperiencehost");
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}

/// <summary>
/// Event arguments for camera status change events.
/// </summary>
public class CameraStatusChangedEventArgs : EventArgs
{
    public bool IsInUse { get; }
    public List<string> ActiveApplications { get; }

    public CameraStatusChangedEventArgs(bool isInUse, List<string> activeApplications)
    {
        IsInUse = isInUse;
        ActiveApplications = activeApplications;
    }
}
