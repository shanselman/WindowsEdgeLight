using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WindowsEdgeLight;

public partial class MainWindow : Window
{
    private bool isLightOn = true;
    private double currentOpacity = 1.0;  // Full brightness by default
    private const double OpacityStep = 0.15;
    private const double MinOpacity = 0.2;
    private const double MaxOpacity = 1.0;

    // Color temperature ("cool" blue-ish to "warm" amber-ish)
    // We'll model this as a simple 0-1 slider where 0 = coolest, 1 = warmest.
    private double _colorTemperature = 0.5;
    private const double ColorTempStep = 0.1;
    private const double MinColorTemp = 0.0;
    private const double MaxColorTemp = 1.0;

    // DPI Scale
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    
    private bool _isManualMonitorSwitch = false;
    
    private NotifyIcon? notifyIcon;
    private ControlWindow? controlWindow;
    // Tracks whether the control window should be visible (controls initial visibility and toggle state)
    private bool isControlWindowVisible = true;
    private ToolStripMenuItem? toggleControlsMenuItem;

    private class MonitorWindowContext
    {
        public Window Window { get; set; } = null!;
        public Screen Screen { get; set; } = null!;
        public System.Windows.Shapes.Path BorderPath { get; set; } = null!;
        public Ellipse HoverRing { get; set; } = null!;
        public Geometry BaseGeometry { get; set; } = null!;
        public Rect FrameOuterRect { get; set; }
        public Rect FrameInnerRect { get; set; }
        public double PathOffsetX { get; set; }
        public double PathOffsetY { get; set; }
        public double DpiScaleX { get; set; } = 1.0;
        public double DpiScaleY { get; set; } = 1.0;
    }

    // Monitor management
    private int currentMonitorIndex = 0;
    private Screen[] availableMonitors = Array.Empty<Screen>();
    private bool showOnAllMonitors = false;
    private List<MonitorWindowContext> additionalMonitorWindows = new List<MonitorWindowContext>();

    // Global hotkey IDs
    private const int HOTKEY_TOGGLE = 1;
    private const int HOTKEY_BRIGHTNESS_UP = 2;
    private const int HOTKEY_BRIGHTNESS_DOWN = 3;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;  // lowercase to match existing usage
        public int y;
    }

    // Mouse hook P/Invoke declarations
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Mouse hook management
    private IntPtr mouseHookHandle = IntPtr.Zero;
    private LowLevelMouseProc? mouseHookCallback;

    private Rect? frameOuterRect;
    private Rect? frameInnerRect;
    private readonly Ellipse? hoverCursorRing;
    // Added fields for hole effect
    private Geometry? baseFrameGeometry; // original frame geometry (outer minus inner)
    private double pathOffsetX; // offset of geometry within window
    private double pathOffsetY;

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_L = 0x4C;
    private const uint VK_UP = 0x26;
    private const uint VK_DOWN = 0x28;

    public MainWindow()
    {
        InitializeComponent();
        hoverCursorRing = FindName("HoverCursorRing") as Ellipse;
        SetupNotifyIcon();
    }

    private void SetupNotifyIcon()
    {
        notifyIcon = new NotifyIcon();
        
        // Load icon from embedded resource or file
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "ringlight_cropped.ico");
            if (File.Exists(iconPath))
            {
                notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                // Try application icon from exe
                var appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
                notifyIcon.Icon = appIcon ?? System.Drawing.SystemIcons.Application;
            }
        }
        catch (Exception)
        {
            // Fallback to default icon if loading fails
            notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }
        
        notifyIcon.Text = "Windows Edge Light - Right-click for options";
        notifyIcon.Visible = true;
        
    var contextMenu = new ContextMenuStrip();
    contextMenu.Items.Add("📋 Keyboard Shortcuts", null, (s, e) => ShowHelp());
    contextMenu.Items.Add(new ToolStripSeparator());
    contextMenu.Items.Add("💡 Toggle Light (Ctrl+Shift+L)", null, (s, e) => ToggleLight());
    contextMenu.Items.Add("🔆 Brightness Up (Ctrl+Shift+↑)", null, (s, e) => IncreaseBrightness());
    contextMenu.Items.Add("🔅 Brightness Down (Ctrl+Shift+↓)", null, (s, e) => DecreaseBrightness());
    contextMenu.Items.Add(new ToolStripSeparator());
    contextMenu.Items.Add("🔥 K- Warmer Light", null, (s, e) => IncreaseColorTemperature());
    contextMenu.Items.Add("❄️ K+ Cooler Light", null, (s, e) => DecreaseColorTemperature());
    contextMenu.Items.Add(new ToolStripSeparator());
    contextMenu.Items.Add("🖥️ Switch Monitor", null, (s, e) => MoveToNextMonitor());
    contextMenu.Items.Add("🖥️🖥️ Toggle All Monitors", null, (s, e) => ToggleAllMonitors());
    contextMenu.Items.Add(new ToolStripSeparator());
    
    // Add toggle controls menu item - text will be set by UpdateTrayMenuToggleControlsText
    toggleControlsMenuItem = new ToolStripMenuItem("🎛️ Hide Controls", null, (s, e) => ToggleControlsVisibility());
    contextMenu.Items.Add(toggleControlsMenuItem);
    
    contextMenu.Items.Add(new ToolStripSeparator());
    contextMenu.Items.Add("✖ Exit", null, (s, e) => System.Windows.Application.Current.Shutdown());
        
        notifyIcon.ContextMenuStrip = contextMenu;
        notifyIcon.DoubleClick += (s, e) => ShowHelp();
        
        // Set initial menu text based on current state
        UpdateTrayMenuToggleControlsText();
    }

    private void ShowHelp()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "Unknown";
        
        var helpMessage = $@"Windows Edge Light - Keyboard Shortcuts

💡 Toggle Light:  Ctrl + Shift + L
🔆 Brightness Up:  Ctrl + Shift + ↑
🔅 Brightness Down:  Ctrl + Shift + ↓

💡 Features:
• Click-through overlay - won't interfere with your work
• Global hotkeys work from any application
• Right-click taskbar icon for full menu
• Control toolbar with brightness, color temp, and monitor options
• Color temperature controls (🔥 warmer, ❄️ cooler)
• Switch between monitors or show on all monitors

Created by Scott Hanselman
Version {version}";

        System.Windows.MessageBox.Show(helpMessage, "Windows Edge Light - Help", 
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SetupWindow()
    {
        // Initialize available monitors on first setup
        if (availableMonitors.Length == 0)
        {
            availableMonitors = Screen.AllScreens;
            
            // Find the primary monitor index
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

        SetupWindowForScreen(targetScreen);
    }

    private void SetupWindowForScreen(Screen screen)
    {
        // Use WorkingArea instead of Bounds to exclude taskbar
        var workingArea = screen.WorkingArea;
        
        // Get DPI scale factor
        var source = PresentationSource.FromVisual(this);
        _dpiScaleX = 1.0;
        _dpiScaleY = 1.0;
        
        if (source != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }
        
        // Convert physical pixels to WPF DIPs
        this.Left = workingArea.X / _dpiScaleX;
        this.Top = workingArea.Y / _dpiScaleY;
        this.Width = workingArea.Width / _dpiScaleX;
        this.Height = workingArea.Height / _dpiScaleY;
        this.WindowState = System.Windows.WindowState.Normal;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        CreateFrameGeometry();
        CreateControlWindow();
        
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        
        // Register global hotkeys
        RegisterHotKey(hwnd, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_L);
        RegisterHotKey(hwnd, HOTKEY_BRIGHTNESS_UP, MOD_CONTROL | MOD_SHIFT, VK_UP);
        RegisterHotKey(hwnd, HOTKEY_BRIGHTNESS_DOWN, MOD_CONTROL | MOD_SHIFT, VK_DOWN);
        
        // Hook into Windows message processing
        HwndSource source = HwndSource.FromHwnd(hwnd);
        source.AddHook(HwndHook);
        
        // Listen for window size/location changes (docking/undocking)
        this.SizeChanged += Window_SizeChanged;
        this.LocationChanged += Window_LocationChanged;

        InstallMouseHook();
    }

    private void InstallMouseHook()
    {
        // Store callback to prevent garbage collection
        mouseHookCallback = MouseHookProc;
        
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule != null)
        {
            mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, mouseHookCallback, 
                GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private void UninstallMouseHook()
    {
        if (mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHookHandle);
            mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            
            // Dispatch to UI thread for WPF operations
            Dispatcher.BeginInvoke(new Action(() => 
            {
                HandleMouseMove(hookStruct.pt.x, hookStruct.pt.y);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
    }

    private void HandleMouseMove(int screenX, int screenY)
    {
        if (!isLightOn)
        {
            if (EdgeLightBorder.Visibility != Visibility.Collapsed)
            {
                EdgeLightBorder.Visibility = Visibility.Collapsed;
            }

            if (hoverCursorRing != null && hoverCursorRing.Visibility != Visibility.Collapsed)
            {
                hoverCursorRing.Visibility = Visibility.Collapsed;
            }
            // Restore original geometry if previously punched
            if (baseFrameGeometry != null && EdgeLightBorder.Data != baseFrameGeometry)
            {
                EdgeLightBorder.Data = baseFrameGeometry;
            }

            // Also handle additional windows
            foreach (var ctx in additionalMonitorWindows)
            {
                if (ctx.BorderPath.Visibility != Visibility.Collapsed)
                    ctx.BorderPath.Visibility = Visibility.Collapsed;
                if (ctx.HoverRing.Visibility != Visibility.Collapsed)
                    ctx.HoverRing.Visibility = Visibility.Collapsed;
                if (ctx.BorderPath.Data != ctx.BaseGeometry)
                    ctx.BorderPath.Data = ctx.BaseGeometry;
            }

            return;
        }

        // --- Main Window Logic ---
        if (frameOuterRect != null && frameInnerRect != null && hoverCursorRing != null && baseFrameGeometry != null)
        {
            var screen = availableMonitors.Length > 0 ? availableMonitors[currentMonitorIndex] : Screen.PrimaryScreen;
            if (screen != null)
            {
                ApplyHolePunchEffect(
                    screenX, screenY,
                    screen,
                    _dpiScaleX, _dpiScaleY,
                    frameOuterRect.Value, frameInnerRect.Value,
                    hoverCursorRing,
                    EdgeLightBorder,
                    baseFrameGeometry,
                    pathOffsetX, pathOffsetY,
                    (ring, x, y) => { Canvas.SetLeft(ring, x); Canvas.SetTop(ring, y); }
                );
            }
        }

        // --- Additional Windows Logic ---
        foreach (var ctx in additionalMonitorWindows)
        {
            try
            {
                ApplyHolePunchEffect(
                    screenX, screenY,
                    ctx.Screen,
                    ctx.DpiScaleX, ctx.DpiScaleY,
                    ctx.FrameOuterRect, ctx.FrameInnerRect,
                    ctx.HoverRing,
                    ctx.BorderPath,
                    ctx.BaseGeometry,
                    ctx.PathOffsetX, ctx.PathOffsetY,
                    (ring, x, y) => { ring.Margin = new Thickness(x, y, 0, 0); }
                );
            }
            catch (InvalidOperationException)
            {
                // Can happen if window is closing or not ready
            }
        }
    }

    private void ApplyHolePunchEffect(
        int screenX, int screenY,
        Screen screen,
        double dpiScaleX, double dpiScaleY,
        Rect frameOuterRect, Rect frameInnerRect,
        Ellipse hoverRing,
        System.Windows.Shapes.Path borderPath,
        Geometry baseGeometry,
        double pathOffsetX, double pathOffsetY,
        Action<Ellipse, double, double> positionRing)
    {
        // Manual coordinate calculation to avoid PointFromScreen issues across monitors/DPIs
        // We positioned the window using dpiScaleX/Y relative to the screen WorkingArea.
        double relX = (screenX - screen.WorkingArea.X) / dpiScaleX;
        double relY = (screenY - screen.WorkingArea.Y) / dpiScaleY;
        var windowPt = new System.Windows.Point(relX, relY);

        // Existing frame band detection (outer minus inner)
        bool inFrameBand = frameOuterRect.Contains(windowPt) && !frameInnerRect.Contains(windowPt);

        // Early detection zone just inside the inner edge: a band with thickness = hole radius (cursor ring radius)
        double ringDiameter = hoverRing.Width;
        double holeRadius = ringDiameter / 2; // match ring size
        var innerProximityRect = new Rect(
            frameInnerRect.X + holeRadius,
            frameInnerRect.Y + holeRadius,
            frameInnerRect.Width - (holeRadius * 2),
            frameInnerRect.Height - (holeRadius * 2));

        // Near from inside means inside innerRect but within holeRadius of its edge (i.e., not deep inside innerProximityRect)
        bool nearFromInside = frameInnerRect.Contains(windowPt) && !innerProximityRect.Contains(windowPt);

        bool overFrame = inFrameBand || nearFromInside;

        if (overFrame)
        {
            positionRing(hoverRing, windowPt.X - ringDiameter / 2, windowPt.Y - ringDiameter / 2);
            
            if (hoverRing.Visibility != Visibility.Visible)
            {
                hoverRing.Visibility = Visibility.Visible;
            }

            // Punch a transparent hole under the ring by excluding a circle geometry from the frame
            // Convert window coordinates to geometry local coordinates by subtracting stored offsets
            var localCenter = new System.Windows.Point(windowPt.X - pathOffsetX, windowPt.Y - pathOffsetY);
            var hole = new EllipseGeometry(localCenter, holeRadius, holeRadius);
            borderPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, baseGeometry, hole);
        }
        else
        {
            if (hoverRing.Visibility != Visibility.Collapsed)
            {
                hoverRing.Visibility = Visibility.Collapsed;
            }

            if (borderPath.Visibility != Visibility.Visible)
            {
                borderPath.Visibility = Visibility.Visible;
            }
            // Restore original geometry (remove hole)
            if (baseGeometry != null && borderPath.Data != baseGeometry)
            {
                borderPath.Data = baseGeometry;
            }
        }
    }

    private void CreateControlWindow()
    {
        controlWindow = new ControlWindow(this);
        RepositionControlWindow();
        
        // Only show if controls are supposed to be visible
        if (isControlWindowVisible)
        {
            controlWindow.Show();
        }
    }

    private void CreateFrameGeometry()
    {
        // Get actual dimensions (accounting for margin)
        double width = this.ActualWidth - 40;  // 20px margin on each side
        double height = this.ActualHeight - 40;
        
        const double frameThickness = 80;
        const double outerRadius = 100;  // Extra rounded like macOS
        const double innerRadius = 60;   // Keep proportional
        
        // Outer rounded rectangle
        var outerRect = new RectangleGeometry(new Rect(0, 0, width, height), outerRadius, outerRadius);
        
        // Inner rounded rectangle
        var innerRect = new RectangleGeometry(
            new Rect(frameThickness, frameThickness, 
                    width - (frameThickness * 2), 
                    height - (frameThickness * 2)), 
            innerRadius, innerRadius);
        
        // Combine: outer minus inner = frame
        var frameGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);
        baseFrameGeometry = frameGeometry; // store original
        EdgeLightBorder.Data = frameGeometry;
        pathOffsetX = (ActualWidth - width) / 2.0; // store offsets for local coordinate conversion
        pathOffsetY = (ActualHeight - height) / 2.0;
        // Expand outer and contract inner rects for earlier hover detection based on ring hole radius.
        double ringDiameter = hoverCursorRing?.Width ?? 0;
        double holeRadius = ringDiameter / 2.0;
        frameOuterRect = new Rect(pathOffsetX - holeRadius, pathOffsetY - holeRadius, width + holeRadius * 2, height + holeRadius * 2);
        frameInnerRect = new Rect(pathOffsetX + frameThickness + holeRadius, pathOffsetY + frameThickness + holeRadius, width - (frameThickness * 2) - holeRadius * 2, height - (frameThickness * 2) - holeRadius * 2);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            
            switch (hotkeyId)
            {
                case HOTKEY_TOGGLE:
                    ToggleLight();
                    handled = true;
                    break;
                case HOTKEY_BRIGHTNESS_UP:
                    IncreaseBrightness();
                    handled = true;
                    break;
                case HOTKEY_BRIGHTNESS_DOWN:
                    DecreaseBrightness();
                    handled = true;
                    break;
            }
        }
        
        return IntPtr.Zero;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        
        _dpiScaleX = newDpi.DpiScaleX;
        _dpiScaleY = newDpi.DpiScaleY;
        
        // Ensure window covers the current monitor correctly with new DPI
        if (availableMonitors.Length > 0)
        {
            // Re-verify which monitor we are on, as we might have just moved
            UpdateCurrentMonitorIndex();
            
            if (currentMonitorIndex < availableMonitors.Length)
            {
                var screen = availableMonitors[currentMonitorIndex];
                var workingArea = screen.WorkingArea;
                
                // Only update if significantly different to avoid loops
                double newLeft = workingArea.X / _dpiScaleX;
                double newTop = workingArea.Y / _dpiScaleY;
                double newWidth = workingArea.Width / _dpiScaleX;
                double newHeight = workingArea.Height / _dpiScaleY;

                if (Math.Abs(this.Left - newLeft) > 1 || Math.Abs(this.Top - newTop) > 1 ||
                    Math.Abs(this.Width - newWidth) > 1 || Math.Abs(this.Height - newHeight) > 1)
                {
                    this.Left = newLeft;
                    this.Top = newTop;
                    this.Width = newWidth;
                    this.Height = newHeight;
                }
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        UninstallMouseHook();
        
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HOTKEY_TOGGLE);
        UnregisterHotKey(hwnd, HOTKEY_BRIGHTNESS_UP);
        UnregisterHotKey(hwnd, HOTKEY_BRIGHTNESS_DOWN);
        
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        
        HideAdditionalMonitorWindows();
        controlWindow?.Close();
        
        base.OnClosed(e);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.L && 
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && 
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            ToggleLight();
        }
        else if (e.Key == Key.Escape)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleLight();
    }

    private void ToggleLight()
    {
        isLightOn = !isLightOn;
        if (isLightOn)
        {
            EdgeLightBorder.Visibility = Visibility.Visible;
            // Restore base geometry on toggle if needed
            if (baseFrameGeometry != null)
            {
                EdgeLightBorder.Data = baseFrameGeometry;
            }
        }
        else
        {
            EdgeLightBorder.Visibility = Visibility.Collapsed;
            if (hoverCursorRing != null)
            {
                hoverCursorRing.Visibility = Visibility.Collapsed;
            }
        }
        
        // Update all additional monitor windows
        UpdateAdditionalMonitorWindows();
    }

    public void HandleToggle()
    {
        ToggleLight();
    }

    public void ToggleControlsVisibility()
    {
        isControlWindowVisible = !isControlWindowVisible;
        
        // Apply visibility change if control window exists
        if (controlWindow != null)
        {
            if (isControlWindowVisible)
            {
                controlWindow.Show();
            }
            else
            {
                controlWindow.Hide();
            }
        }
        // Note: If controlWindow doesn't exist yet, isControlWindowVisible state
        // is preserved and will be applied when CreateControlWindow() is called
        
        UpdateTrayMenuToggleControlsText();
    }

    private void UpdateTrayMenuToggleControlsText()
    {
        if (toggleControlsMenuItem != null)
        {
            toggleControlsMenuItem.Text = isControlWindowVisible ? "🎛️ Hide Controls" : "🎛️ Show Controls";
        }
    }

    public void IncreaseBrightness()
    {
        currentOpacity = Math.Min(MaxOpacity, currentOpacity + OpacityStep);
        EdgeLightBorder.Opacity = currentOpacity;
        
        // Update all additional monitor windows
        UpdateAdditionalMonitorWindows();
    }

    public void DecreaseBrightness()
    {
        currentOpacity = Math.Max(MinOpacity, currentOpacity - OpacityStep);
        EdgeLightBorder.Opacity = currentOpacity;
        
        // Update all additional monitor windows
        UpdateAdditionalMonitorWindows();
    }

    private void UpdateAdditionalMonitorWindows()
    {
        foreach (var ctx in additionalMonitorWindows)
        {
            var path = ctx.BorderPath;
            path.Opacity = currentOpacity;
            path.Visibility = isLightOn ? Visibility.Visible : Visibility.Collapsed;
            
            // Update color temperature
            if (path.Fill is LinearGradientBrush brush && brush.GradientStops.Count >= 3)
            {
                var cool = System.Windows.Media.Color.FromRgb(220, 235, 255);
                var warm = System.Windows.Media.Color.FromRgb(255, 220, 180);
                
                System.Windows.Media.Color Lerp(System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
                {
                    byte LerpByte(byte x, byte y, double tt) => (byte)(x + (y - x) * tt);
                    return System.Windows.Media.Color.FromArgb(255, LerpByte(a.R, b.R, t), LerpByte(a.G, b.G, t), LerpByte(a.B, b.B, t));
                }
                
                var midColor = Lerp(cool, warm, _colorTemperature);
                
                foreach (var stop in brush.GradientStops)
                {
                    if (stop.Offset is > 0.2 and < 0.8)
                    {
                        stop.Color = midColor;
                    }
                }
            }
        }
    }

    public void IncreaseColorTemperature()
    {
        SetColorTemperature(_colorTemperature + ColorTempStep);
    }

    public void DecreaseColorTemperature()
    {
        SetColorTemperature(_colorTemperature - ColorTempStep);
    }

    public void SetColorTemperature(double value)
    {
        _colorTemperature = Math.Max(MinColorTemp, Math.Min(MaxColorTemp, value));

        // Map 0-1 slider to a simple cool-to-warm gradient.
        // We'll bias the inner gradient stops from blueish-white (cool) to amber (warm).
        // NOTE: This assumes the brush defined in XAML is still a LinearGradientBrush.
        if (EdgeLightBorder.Fill is LinearGradientBrush brush && brush.GradientStops.Count >= 3)
        {
            // Cool: RGB ~ (220, 235, 255), Warm: RGB ~ (255, 220, 180)
            System.Windows.Media.Color Lerp(System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
            {
                byte LerpByte(byte x, byte y, double tt) => (byte)(x + (y - x) * tt);

                return System.Windows.Media.Color.FromArgb(
                    255,
                    LerpByte(a.R, b.R, t),
                    LerpByte(a.G, b.G, t),
                    LerpByte(a.B, b.B, t));
            }

            var cool = System.Windows.Media.Color.FromRgb(220, 235, 255);
            var warm = System.Windows.Media.Color.FromRgb(255, 220, 180);

            var midColor = Lerp(cool, warm, _colorTemperature);

            // Update a couple of inner stops to shift perceived temperature
            // Keep outer rim relatively neutral for consistent edge.
            foreach (var stop in brush.GradientStops)
            {
                if (stop.Offset is > 0.2 and < 0.8)
                {
                    stop.Color = midColor;
                }
            }
        }
        
        // Update all additional monitor windows
        UpdateAdditionalMonitorWindows();
    }

    public void MoveToNextMonitor()
    {
        // If in all monitors mode, do nothing
        if (showOnAllMonitors) return;
        
        // Refresh monitor list in case of hot-plug/unplug
        availableMonitors = Screen.AllScreens;

        if (availableMonitors.Length <= 1)
        {
            // Only one monitor, nothing to do
            return;
        }

        // Ensure currentMonitorIndex is accurate before moving
        UpdateCurrentMonitorIndex();

        _isManualMonitorSwitch = true;
        try
        {
            // Cycle to next monitor
            currentMonitorIndex = (currentMonitorIndex + 1) % availableMonitors.Length;
            var targetScreen = availableMonitors[currentMonitorIndex];

            // Reposition main window to new monitor using physical coordinates to trigger DPI change correctly
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, IntPtr.Zero, 
                targetScreen.WorkingArea.X, targetScreen.WorkingArea.Y, 
                targetScreen.WorkingArea.Width, targetScreen.WorkingArea.Height, 
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            
            // Force a size update if DPI didn't change (SetWindowPos might not trigger OnDpiChanged)
            // If DPI changed, OnDpiChanged will handle it.
            // But we can't easily know if OnDpiChanged fired yet.
            // However, setting properties to the same value is cheap in WPF.
            // We need to ensure we use the NEW DPI if it changed.
            // OnDpiChanged updates _dpiScaleX/Y.
            
            // If we are on the same thread, OnDpiChanged (via WM_DPICHANGED) should have fired synchronously during SetWindowPos.
            // So _dpiScaleX/Y should be up to date.
            
            double newLeft = targetScreen.WorkingArea.X / _dpiScaleX;
            double newTop = targetScreen.WorkingArea.Y / _dpiScaleY;
            double newWidth = targetScreen.WorkingArea.Width / _dpiScaleX;
            double newHeight = targetScreen.WorkingArea.Height / _dpiScaleY;

            this.Left = newLeft;
            this.Top = newTop;
            this.Width = newWidth;
            this.Height = newHeight;
        }
        finally
        {
            _isManualMonitorSwitch = false;
        }
        
        // Reposition control window to follow
        RepositionControlWindow();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    public void ToggleAllMonitors()
    {
        showOnAllMonitors = !showOnAllMonitors;
        
        if (showOnAllMonitors)
        {
            ShowOnAllMonitors();
        }
        else
        {
            HideAdditionalMonitorWindows();
        }

        controlWindow?.UpdateAllMonitorsButtonState();
    }

    private void ShowOnAllMonitors()
    {
        // Refresh monitor list
        availableMonitors = Screen.AllScreens;

        // Close any existing additional windows
        HideAdditionalMonitorWindows();

        // Create a window for each monitor except the current one (this window)
        for (int i = 0; i < availableMonitors.Length; i++)
        {
            if (i != currentMonitorIndex)
            {
                var monitorCtx = CreateMonitorWindow(availableMonitors[i]);
                additionalMonitorWindows.Add(monitorCtx);
                monitorCtx.Window.Show();
            }
        }
    }

    private void HideAdditionalMonitorWindows()
    {
        foreach (var ctx in additionalMonitorWindows)
        {
            ctx.Window.Close();
        }
        additionalMonitorWindows.Clear();
    }

    private MonitorWindowContext CreateMonitorWindow(Screen screen)
    {
        var window = new Window
        {
            Title = "Windows Edge Light",
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStyle = WindowStyle.None
        };

        // Position on the target screen
        var workingArea = screen.WorkingArea;
        
        // Get the correct DPI scale for THIS specific screen
        var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);
        
        // Convert physical pixels to WPF DIPs using the correct per-monitor DPI
        window.Left = workingArea.X / screenDpiX;
        window.Top = workingArea.Y / screenDpiY;
        window.Width = workingArea.Width / screenDpiX;
        window.Height = workingArea.Height / screenDpiY;

        // Create the grid and edge light border
        var grid = new System.Windows.Controls.Grid { IsHitTestVisible = false };
        var path = new System.Windows.Shapes.Path
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Stretch = System.Windows.Media.Stretch.None,
            Opacity = currentOpacity,
            Visibility = isLightOn ? Visibility.Visible : Visibility.Collapsed
        };

        // Create gradient brush
        var gradient = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(255, 255, 255), 0.0));
        gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(240, 240, 240), 0.3));
        gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(255, 255, 255), 0.5));
        gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(240, 240, 240), 0.7));
        gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(255, 255, 255), 1.0));
        path.Fill = gradient;

        // Add drop shadow effect
        path.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 76,
            Opacity = 1,
            ShadowDepth = 0,
            Color = System.Windows.Media.Color.FromRgb(255, 255, 255)
        };

        // Create hover ring (Ellipse)
        var hoverRing = new Ellipse
        {
            Width = 140,
            Height = 140,
            Fill = System.Windows.Media.Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top
        };
        // Add drop shadow to ring - actually main window doesn't seem to have this on the ring itself?
        // Main window XAML doesn't show effect on HoverCursorRing.
        // So removing effect to match.
        
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
            DpiScaleX = screenDpiX, // Use calculated DPI for this screen
            DpiScaleY = screenDpiY
        };

        // Make window click-through and handle DPI
        window.Loaded += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);

            // Verify and update DPI if WPF reports a different value after window is loaded
            var source = PresentationSource.FromVisual(window);
            if (source != null)
            {
                double dpiX = source.CompositionTarget.TransformToDevice.M11;
                double dpiY = source.CompositionTarget.TransformToDevice.M22;
                
                // Only reposition if DPI changed significantly from our initial calculation
                if (Math.Abs(dpiX - ctx.DpiScaleX) > 0.01 || Math.Abs(dpiY - ctx.DpiScaleY) > 0.01)
                {
                    ctx.DpiScaleX = dpiX;
                    ctx.DpiScaleY = dpiY;

                    // Reposition/Resize with correct DPI
                    window.Left = screen.WorkingArea.X / dpiX;
                    window.Top = screen.WorkingArea.Y / dpiY;
                    window.Width = screen.WorkingArea.Width / dpiX;
                    window.Height = screen.WorkingArea.Height / dpiY;

                    // Recalculate geometry for new window size
                    UpdateMonitorGeometry(ctx);
                }
            }
        };

        return ctx;
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

    public bool IsShowingOnAllMonitors()
    {
        return showOnAllMonitors;
    }

    private void RepositionControlWindow()
    {
        if (controlWindow == null) return;

        // Position at bottom center of main window
        controlWindow.Left = this.Left + (this.Width - controlWindow.Width) / 2;
        controlWindow.Top = this.Top + this.Height - controlWindow.Height - 124;
    }

    public bool HasMultipleMonitors()
    {
        // Refresh monitor count to handle hot-plug scenarios
        availableMonitors = Screen.AllScreens;
        return availableMonitors.Length > 1;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Recreate geometry when window size changes (e.g., different monitor resolution)
        if (EdgeLightBorder != null)
        {
            CreateFrameGeometry();
        }
        
        // Reposition control window
        RepositionControlWindow();
        
        // Update which monitor we're actually on
        UpdateCurrentMonitorIndex();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        // Reposition control window when main window moves
        RepositionControlWindow();
        
        // Update which monitor we're actually on
        UpdateCurrentMonitorIndex();
    }

    private void UpdateCurrentMonitorIndex()
    {
        // If we are manually switching, trust the index we set explicitly
        if (_isManualMonitorSwitch) return;

        // Refresh monitor list
        availableMonitors = Screen.AllScreens;
        
        if (availableMonitors.Length == 0) return;

        try 
        {
            // Use PointToScreen to get accurate physical coordinates of the window center
            // This handles DPI scaling correctly unlike manual calculation
            var centerPoint = this.PointToScreen(new System.Windows.Point(this.ActualWidth / 2, this.ActualHeight / 2));
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
            // Window might not be loaded or visible yet
        }
    }
    
    private (double dpiScaleX, double dpiScaleY) GetDpiForScreen(Screen screen)
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

    private void BrightnessUp_Click(object sender, RoutedEventArgs e)
    {
        IncreaseBrightness();
    }

    private void BrightnessDown_Click(object sender, RoutedEventArgs e)
    {
        DecreaseBrightness();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}