using System.Runtime.InteropServices;
using WindowsEdgeLight.Services.Interfaces;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Service for managing monitor detection and multi-monitor operations.
/// </summary>
public class MonitorService : IMonitorService
{
    #region P/Invoke Declarations

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion

    /// <inheritdoc/>
    public Screen[] GetAvailableMonitors()
    {
        return Screen.AllScreens;
    }

    /// <inheritdoc/>
    public Screen GetPrimaryMonitor()
    {
        return Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault() ?? throw new InvalidOperationException("No monitors detected");
    }

    /// <inheritdoc/>
    public (double scaleX, double scaleY) GetDpiForScreen(Screen screen)
    {
        try
        {
            // Get monitor handle for the center of the screen
            var centerPoint = new POINT
            {
                x = screen.Bounds.X + screen.Bounds.Width / 2,
                y = screen.Bounds.Y + screen.Bounds.Height / 2
            };

            IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);

            if (hMonitor != IntPtr.Zero)
            {
                int result = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
                if (result == 0) // S_OK
                {
                    // Convert from DPI to scale factor (96 DPI = 100% = 1.0)
                    return (dpiX / 96.0, dpiY / 96.0);
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        // Fallback: return 1.0 (100% scaling)
        return (1.0, 1.0);
    }

    /// <inheritdoc/>
    public bool HasMultipleMonitors()
    {
        return Screen.AllScreens.Length > 1;
    }
}
