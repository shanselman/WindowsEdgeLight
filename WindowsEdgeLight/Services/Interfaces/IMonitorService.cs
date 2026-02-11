namespace WindowsEdgeLight.Services.Interfaces;

/// <summary>
/// Service for managing monitor detection and multi-monitor window management.
/// </summary>
public interface IMonitorService
{
    /// <summary>
    /// Gets all available monitors.
    /// </summary>
    /// <returns>Array of available screens.</returns>
    Screen[] GetAvailableMonitors();

    /// <summary>
    /// Gets the primary monitor.
    /// </summary>
    /// <returns>The primary screen.</returns>
    Screen GetPrimaryMonitor();

    /// <summary>
    /// Gets the DPI scale factors for a specific screen.
    /// </summary>
    /// <param name="screen">The screen to get DPI for.</param>
    /// <returns>Tuple of (scaleX, scaleY) DPI factors.</returns>
    (double scaleX, double scaleY) GetDpiForScreen(Screen screen);

    /// <summary>
    /// Determines if multiple monitors are available.
    /// </summary>
    /// <returns>True if more than one monitor is detected.</returns>
    bool HasMultipleMonitors();
}
