using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Manages frame geometry creation and hole punch effects for the edge light frame.
/// </summary>
public class FrameGeometryManager
{
    private readonly MainWindow _mainWindow;
    private Geometry? _baseFrameGeometry;
    private double _pathOffsetX;
    private double _pathOffsetY;
    private Rect? _frameOuterRect;
    private Rect? _frameInnerRect;

    // Mouse hook management
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private LowLevelMouseProc? _mouseHookCallback;

    public FrameGeometryManager(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// Creates the rounded frame geometry for the edge light border.
    /// </summary>
    public void CreateFrameGeometry()
    {
        var edgeLightBorder = _mainWindow.FindName("EdgeLightBorder") as Path;
        if (edgeLightBorder == null) return;

        double width = _mainWindow.ActualWidth - 40;
        double height = _mainWindow.ActualHeight - 40;
        
        const double frameThickness = 80;
        const double outerRadius = 100;
        const double innerRadius = 60;
        
        var outerRect = new RectangleGeometry(new Rect(0, 0, width, height), outerRadius, outerRadius);
        var innerRect = new RectangleGeometry(
            new Rect(frameThickness, frameThickness, 
                    width - (frameThickness * 2), 
                    height - (frameThickness * 2)), 
            innerRadius, innerRadius);
        
        var frameGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);
        _baseFrameGeometry = frameGeometry;
        edgeLightBorder.Data = frameGeometry;
        
        _pathOffsetX = (_mainWindow.ActualWidth - width) / 2.0;
        _pathOffsetY = (_mainWindow.ActualHeight - height) / 2.0;
        
        var hoverCursorRing = _mainWindow.FindName("HoverCursorRing") as Ellipse;
        if (hoverCursorRing != null)
        {
            double ringDiameter = hoverCursorRing.Width;
            double holeRadius = ringDiameter / 2.0;
            _frameOuterRect = new Rect(_pathOffsetX - holeRadius, _pathOffsetY - holeRadius, width + holeRadius * 2, height + holeRadius * 2);
            _frameInnerRect = new Rect(_pathOffsetX + frameThickness + holeRadius, _pathOffsetY + frameThickness + holeRadius, width - (frameThickness * 2) - holeRadius * 2, height - (frameThickness * 2) - holeRadius * 2);
        }
    }

    /// <summary>
    /// Installs the global mouse hook for tracking cursor position.
    /// </summary>
    public void InstallMouseHook()
    {
        _mouseHookCallback = MouseHookProc;
        
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule != null)
        {
            _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookCallback, 
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    /// <summary>
    /// Uninstalls the global mouse hook.
    /// </summary>
    public void UninstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            
            _mainWindow.Dispatcher.BeginInvoke(new Action(() => 
            {
                HandleMouseMove(hookStruct.pt.x, hookStruct.pt.y);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void HandleMouseMove(int screenX, int screenY)
    {
        var viewModel = _mainWindow.DataContext as WindowsEdgeLight.ViewModels.MainViewModel;
        if (viewModel == null || !viewModel.IsLightOn)
        {
            var edgeLightBorder = _mainWindow.FindName("EdgeLightBorder") as Path;
            var hoverCursorRing = _mainWindow.FindName("HoverCursorRing") as Ellipse;

            if (edgeLightBorder != null && edgeLightBorder.Visibility != Visibility.Collapsed)
                edgeLightBorder.Visibility = Visibility.Collapsed;

            if (hoverCursorRing != null && hoverCursorRing.Visibility != Visibility.Collapsed)
                hoverCursorRing.Visibility = Visibility.Collapsed;

            if (_baseFrameGeometry != null && edgeLightBorder != null && edgeLightBorder.Data != _baseFrameGeometry)
                edgeLightBorder.Data = _baseFrameGeometry;

            return;
        }

        var edgeLightPath = _mainWindow.FindName("EdgeLightBorder") as Path;
        var hoverRing = _mainWindow.FindName("HoverCursorRing") as Ellipse;

        if (edgeLightPath != null && hoverRing != null && _frameOuterRect.HasValue && _frameInnerRect.HasValue)
        {
            ApplyHolePunchEffect(screenX, screenY, edgeLightPath, hoverRing);
        }
    }

    private void ApplyHolePunchEffect(int screenX, int screenY, Path borderPath, Ellipse hoverRing)
    {
        var screen = Screen.AllScreens.Length > 0 ? Screen.AllScreens[0] : Screen.PrimaryScreen;
        if (screen == null) return;

        var source = PresentationSource.FromVisual(_mainWindow);
        double dpiScaleX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;

        double relX = (screenX - screen.WorkingArea.X) / dpiScaleX;
        double relY = (screenY - screen.WorkingArea.Y) / dpiScaleY;
        var windowPt = new Point(relX, relY);

        bool inFrameBand = _frameOuterRect!.Value.Contains(windowPt) && !_frameInnerRect!.Value.Contains(windowPt);

        double ringDiameter = hoverRing.Width;
        double holeRadius = ringDiameter / 2.0;
        var innerProximityRect = new Rect(
            _frameInnerRect!.Value.X + holeRadius,
            _frameInnerRect!.Value.Y + holeRadius,
            _frameInnerRect!.Value.Width - (holeRadius * 2),
            _frameInnerRect!.Value.Height - (holeRadius * 2));

        bool nearFromInside = _frameInnerRect!.Value.Contains(windowPt) && !innerProximityRect.Contains(windowPt);
        bool overFrame = inFrameBand || nearFromInside;

        if (overFrame)
        {
            Canvas.SetLeft(hoverRing, windowPt.X - ringDiameter / 2);
            Canvas.SetTop(hoverRing, windowPt.Y - ringDiameter / 2);
            
            if (hoverRing.Visibility != Visibility.Visible)
                hoverRing.Visibility = Visibility.Visible;

            var localCenter = new Point(windowPt.X - _pathOffsetX, windowPt.Y - _pathOffsetY);
            var hole = new EllipseGeometry(localCenter, holeRadius, holeRadius);
            borderPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, _baseFrameGeometry, hole);
        }
        else
        {
            if (hoverRing.Visibility != Visibility.Collapsed)
                hoverRing.Visibility = Visibility.Collapsed;

            if (borderPath.Visibility != Visibility.Visible)
                borderPath.Visibility = Visibility.Visible;

            if (_baseFrameGeometry != null && borderPath.Data != _baseFrameGeometry)
                borderPath.Data = _baseFrameGeometry;
        }
    }

    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    #endregion
}
