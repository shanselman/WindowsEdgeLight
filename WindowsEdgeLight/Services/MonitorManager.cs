using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WindowsEdgeLight.Models;
using WindowsEdgeLight.ViewModels;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Manages creation, positioning, and lifecycle of windows on multiple monitors.
/// </summary>
public class MonitorManager
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private List<MonitorWindowContext> _additionalMonitorWindows = new();

    public MonitorManager(MainViewModel viewModel, AppSettings settings)
    {
        _viewModel = viewModel;
        _settings = settings;
    }

    public IReadOnlyList<MonitorWindowContext> AdditionalMonitorWindows => _additionalMonitorWindows.AsReadOnly();

    /// <summary>
    /// Creates and shows windows on all monitors except the primary.
    /// </summary>
    public void ShowOnAllMonitors(Screen[] availableMonitors, int currentMonitorIndex)
    {
        // Close any existing additional windows
        HideAllMonitors();

        // Create a window for each monitor except the current one
        for (int i = 0; i < availableMonitors.Length; i++)
        {
            if (i != currentMonitorIndex)
            {
                var monitorCtx = CreateMonitorWindow(availableMonitors[i]);
                _additionalMonitorWindows.Add(monitorCtx);
                monitorCtx.Window.Show();
            }
        }
    }

    /// <summary>
    /// Closes all additional monitor windows.
    /// </summary>
    public void HideAllMonitors()
    {
        foreach (var ctx in _additionalMonitorWindows)
        {
            ctx.Window.Close();
        }
        _additionalMonitorWindows.Clear();
    }

    /// <summary>
    /// Updates opacity and visibility of all additional monitor windows.
    /// </summary>
    public void UpdateAll()
    {
        foreach (var ctx in _additionalMonitorWindows)
        {
            var path = ctx.BorderPath;
            path.Opacity = _viewModel.CurrentOpacity;
            path.Visibility = _viewModel.IsLightOn ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Creates a new window positioned on the specified monitor.
    /// </summary>
    private MonitorWindowContext CreateMonitorWindow(Screen screen)
    {
        var window = new Window
        {
            Title = "Windows Edge Light",
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStyle = WindowStyle.None
        };

        // Share the ViewModel with the monitor window
        window.DataContext = _viewModel;

        // Position on the target screen
        var workingArea = screen.WorkingArea;
        var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);
        
        window.Left = workingArea.X / screenDpiX;
        window.Top = workingArea.Y / screenDpiY;
        window.Width = workingArea.Width / screenDpiX;
        window.Height = workingArea.Height / screenDpiY;

        // Create the grid and edge light border
        var grid = new Grid { IsHitTestVisible = false };
        var path = new Path
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.None,
            Opacity = _viewModel.CurrentOpacity,
            Visibility = _viewModel.IsLightOn ? Visibility.Visible : Visibility.Collapsed
        };

        // Create gradient brush with bindings to EdgeColor
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        gradient.GradientStops.Add(new GradientStop(Colors.White, 0.0));
        gradient.GradientStops.Add(CreateEdgeColorGradientStop(0.3));
        gradient.GradientStops.Add(CreateEdgeColorGradientStop(0.5));
        gradient.GradientStops.Add(CreateEdgeColorGradientStop(0.7));
        gradient.GradientStops.Add(new GradientStop(Colors.White, 1.0));
        path.Fill = gradient;

        // Add drop shadow effect
        path.Effect = new DropShadowEffect
        {
            BlurRadius = 76,
            Opacity = 1,
            ShadowDepth = 0,
            Color = Colors.White
        };

        // Create hover ring
        var hoverRing = new Ellipse
        {
            Width = 140,
            Height = 140,
            Fill = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        // Create frame geometry
        double width = window.Width - 40;
        double height = window.Height - 40;
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
        path.Data = frameGeometry;

        grid.Children.Add(path);
        grid.Children.Add(hoverRing);
        window.Content = grid;

        // Calculate geometry data for hole punch
        double pathOffsetX = (window.Width - width) / 2.0;
        double pathOffsetY = (window.Height - height) / 2.0;
        
        double ringDiameter = hoverRing.Width;
        double holeRadius = ringDiameter / 2.0;
        var frameOuterRect = new Rect(pathOffsetX - holeRadius, pathOffsetY - holeRadius, width + holeRadius * 2, height + holeRadius * 2);
        var frameInnerRect = new Rect(pathOffsetX + frameThickness + holeRadius, pathOffsetY + frameThickness + holeRadius, width - (frameThickness * 2) - holeRadius * 2, height - (frameThickness * 2) - holeRadius * 2);

        var ctx = new MonitorWindowContext
        {
            Window = window,
            Screen = screen,
            BorderPath = path,
            HoverRing = hoverRing,
            BaseGeometry = frameGeometry,
            FrameOuterRect = frameOuterRect,
            FrameInnerRect = frameInnerRect,
            PathOffsetX = pathOffsetX,
            PathOffsetY = pathOffsetY,
            DpiScaleX = screenDpiX,
            DpiScaleY = screenDpiY
        };

        // Setup window on load
        window.Loaded += (s, e) => OnWindowLoaded(ctx);

        return ctx;
    }

    private void OnWindowLoaded(MonitorWindowContext ctx)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(ctx.Window).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);

        // Apply exclude from capture setting
        var result = SetWindowDisplayAffinity(hwnd, _settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        if (!result)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"Failed to set display affinity for monitor window. Error: {error}");
        }

        // Verify and update DPI if needed
        var source = PresentationSource.FromVisual(ctx.Window);
        if (source != null)
        {
            double dpiX = source.CompositionTarget.TransformToDevice.M11;
            double dpiY = source.CompositionTarget.TransformToDevice.M22;
            
            if (Math.Abs(dpiX - ctx.DpiScaleX) > 0.01 || Math.Abs(dpiY - ctx.DpiScaleY) > 0.01)
            {
                ctx.DpiScaleX = dpiX;
                ctx.DpiScaleY = dpiY;

                var workingArea = ctx.Screen.WorkingArea;
                ctx.Window.Left = workingArea.X / dpiX;
                ctx.Window.Top = workingArea.Y / dpiY;
                ctx.Window.Width = workingArea.Width / dpiX;
                ctx.Window.Height = workingArea.Height / dpiY;

                UpdateMonitorGeometry(ctx);
            }
        }
    }

    private void UpdateMonitorGeometry(MonitorWindowContext ctx)
    {
        double width = ctx.Window.Width - 40;
        double height = ctx.Window.Height - 40;
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
        
        ctx.BaseGeometry = frameGeometry;
        ctx.BorderPath.Data = frameGeometry;
        
        ctx.PathOffsetX = (ctx.Window.Width - width) / 2.0;
        ctx.PathOffsetY = (ctx.Window.Height - height) / 2.0;
        
        double ringDiameter = ctx.HoverRing.Width;
        double holeRadius = ringDiameter / 2.0;
        
        ctx.FrameOuterRect = new Rect(ctx.PathOffsetX - holeRadius, ctx.PathOffsetY - holeRadius, width + holeRadius * 2, height + holeRadius * 2);
        ctx.FrameInnerRect = new Rect(ctx.PathOffsetX + frameThickness + holeRadius, ctx.PathOffsetY + frameThickness + holeRadius, width - (frameThickness * 2) - holeRadius * 2, height - (frameThickness * 2) - holeRadius * 2);
    }

    private GradientStop CreateEdgeColorGradientStop(double offset)
    {
        var stop = new GradientStop(Colors.White, offset);
        System.Windows.Data.BindingOperations.SetBinding(stop, GradientStop.ColorProperty, new System.Windows.Data.Binding(nameof(MainViewModel.EdgeColor))
        {
            Source = _viewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        return stop;
    }

    private (double dpiScaleX, double dpiScaleY) GetDpiForScreen(Screen screen)
    {
        try
        {
            var centerPoint = new POINT
            {
                x = screen.Bounds.X + screen.Bounds.Width / 2,
                y = screen.Bounds.Y + screen.Bounds.Height / 2
            };
            
            IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);
            
            if (hMonitor != IntPtr.Zero)
            {
                int result = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
                if (result == 0)
                {
                    return (dpiX / 96.0, dpiY / 96.0);
                }
            }
        }
        catch
        {
            // Fall through to default
        }
        
        return (1.0, 1.0);
    }

    #region P/Invoke

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    
    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    #endregion
}
