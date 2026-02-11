using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Point = System.Windows.Point;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Manages window positioning, sizing, and DPI-aware layout.
/// </summary>
public class WindowPositioningManager
{
    private readonly MainWindow _mainWindow;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public WindowPositioningManager(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// Initializes window positioning on first load.
    /// </summary>
    public void SetupWindow(Screen[] availableMonitors, ref int currentMonitorIndex)
    {
        if (availableMonitors.Length == 0)
        {
            availableMonitors = Screen.AllScreens;
            
            for (int i = 0; i < availableMonitors.Length; i++)
            {
                if (availableMonitors[i].Primary)
                {
                    currentMonitorIndex = i;
                    break;
                }
            }
        }

        var targetScreen = availableMonitors.Length > 0 ? availableMonitors[currentMonitorIndex] : Screen.PrimaryScreen;
        if (targetScreen == null) return;

        PositionWindowOnScreen(_mainWindow, targetScreen);
    }

    /// <summary>
    /// Positions a window to fill the working area of a specified screen.
    /// </summary>
    public void PositionWindowOnScreen(Window window, Screen screen)
    {
        var workingArea = screen.WorkingArea;
        
        GetDpiForCurrentContext(window);
        
        window.Left = workingArea.X / _dpiScaleX;
        window.Top = workingArea.Y / _dpiScaleY;
        window.Width = workingArea.Width / _dpiScaleX;
        window.Height = workingArea.Height / _dpiScaleY;
        window.WindowState = WindowState.Normal;
    }

    /// <summary>
    /// Handles DPI change events (e.g., when moving window between monitors).
    /// </summary>
    public void HandleDpiChange(DpiScale newDpi, Screen[] availableMonitors, ref int currentMonitorIndex)
    {
        _dpiScaleX = newDpi.DpiScaleX;
        _dpiScaleY = newDpi.DpiScaleY;
        
        UpdateCurrentMonitorIndex(availableMonitors, ref currentMonitorIndex);
        
        if (availableMonitors.Length > 0 && currentMonitorIndex < availableMonitors.Length)
        {
            var screen = availableMonitors[currentMonitorIndex];
            var workingArea = screen.WorkingArea;
            
            double newLeft = workingArea.X / _dpiScaleX;
            double newTop = workingArea.Y / _dpiScaleY;
            double newWidth = workingArea.Width / _dpiScaleX;
            double newHeight = workingArea.Height / _dpiScaleY;

            if (Math.Abs(_mainWindow.Left - newLeft) > 1 || Math.Abs(_mainWindow.Top - newTop) > 1 ||
                Math.Abs(_mainWindow.Width - newWidth) > 1 || Math.Abs(_mainWindow.Height - newHeight) > 1)
            {
                _mainWindow.Left = newLeft;
                _mainWindow.Top = newTop;
                _mainWindow.Width = newWidth;
                _mainWindow.Height = newHeight;
            }
        }
    }

    /// <summary>
    /// Determines which monitor the window is currently on.
    /// </summary>
    public void UpdateCurrentMonitorIndex(Screen[] availableMonitors, ref int currentMonitorIndex)
    {
        if (availableMonitors.Length == 0) return;

        try 
        {
            var centerPoint = _mainWindow.PointToScreen(new Point(_mainWindow.ActualWidth / 2, _mainWindow.ActualHeight / 2));
            var drawingPoint = new System.Drawing.Point((int)centerPoint.X, (int)centerPoint.Y);
            
            for (int i = 0; i < availableMonitors.Length; i++)
            {
                if (availableMonitors[i].Bounds.Contains(drawingPoint))
                {
                    currentMonitorIndex = i;
                    break;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Window might not be loaded yet
        }
    }

    private void GetDpiForCurrentContext(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        _dpiScaleX = 1.0;
        _dpiScaleY = 1.0;
        
        if (source != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
}
