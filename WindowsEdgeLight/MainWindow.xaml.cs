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
    
    private NotifyIcon? notifyIcon;
    private ControlWindow? controlWindow;

    // Monitor management
    private int currentMonitorIndex = 0;
    private Screen[] availableMonitors = Array.Empty<Screen>();

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
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
        contextMenu.Items.Add("✖ Exit", null, (s, e) => System.Windows.Application.Current.Shutdown());
        
        notifyIcon.ContextMenuStrip = contextMenu;
        notifyIcon.DoubleClick += (s, e) => ShowHelp();
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
• Right-click taskbar icon for menu

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
        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;
        
        if (source != null)
        {
            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }
        
        // Convert physical pixels to WPF DIPs
        this.Left = workingArea.X / dpiScaleX;
        this.Top = workingArea.Y / dpiScaleY;
        this.Width = workingArea.Width / dpiScaleX;
        this.Height = workingArea.Height / dpiScaleY;
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

            return;
        }
        if (frameOuterRect == null || frameInnerRect == null || hoverCursorRing == null || baseFrameGeometry == null)
        {
            return;
        }

        var windowPt = PointFromScreen(new System.Windows.Point(screenX, screenY));

        // Existing frame band detection (outer minus inner)
        bool inFrameBand = frameOuterRect.Value.Contains(windowPt) && !frameInnerRect.Value.Contains(windowPt);

        // Early detection zone just inside the inner edge: a band with thickness = hole radius (cursor ring radius)
        double ringDiameter = hoverCursorRing.Width;
        double holeRadius = ringDiameter / 2; // match ring size
        var innerProximityRect = new Rect(
            frameInnerRect.Value.X + holeRadius,
            frameInnerRect.Value.Y + holeRadius,
            frameInnerRect.Value.Width - (holeRadius * 2),
            frameInnerRect.Value.Height - (holeRadius * 2));

        // Near from inside means inside innerRect but within holeRadius of its edge (i.e., not deep inside innerProximityRect)
        bool nearFromInside = frameInnerRect.Value.Contains(windowPt) && !innerProximityRect.Contains(windowPt);

        bool overFrame = inFrameBand || nearFromInside;

        if (overFrame)
        {
            Canvas.SetLeft(hoverCursorRing, windowPt.X - ringDiameter / 2);
            Canvas.SetTop(hoverCursorRing, windowPt.Y - ringDiameter / 2);
            if (hoverCursorRing.Visibility != Visibility.Visible)
            {
                hoverCursorRing.Visibility = Visibility.Visible;
            }

            // Punch a transparent hole under the ring by excluding a circle geometry from the frame
            // Convert window coordinates to geometry local coordinates by subtracting stored offsets
            var localCenter = new System.Windows.Point(windowPt.X - pathOffsetX, windowPt.Y - pathOffsetY);
            var hole = new EllipseGeometry(localCenter, holeRadius, holeRadius);
            EdgeLightBorder.Data = new CombinedGeometry(GeometryCombineMode.Exclude, baseFrameGeometry, hole);
        }
        else
        {
            if (hoverCursorRing.Visibility != Visibility.Collapsed)
            {
                hoverCursorRing.Visibility = Visibility.Collapsed;
            }

            if (EdgeLightBorder.Visibility != Visibility.Visible)
            {
                EdgeLightBorder.Visibility = Visibility.Visible;
            }
            // Restore original geometry (remove hole)
            if (baseFrameGeometry != null && EdgeLightBorder.Data != baseFrameGeometry)
            {
                EdgeLightBorder.Data = baseFrameGeometry;
            }
        }
    }

    private void CreateControlWindow()
    {
        controlWindow = new ControlWindow(this);
        RepositionControlWindow();
        controlWindow.Show();
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
            hoverCursorRing?.Visibility = Visibility.Collapsed;
        }
    }

    public void HandleToggle()
    {
        ToggleLight();
    }

    public void IncreaseBrightness()
    {
        currentOpacity = Math.Min(MaxOpacity, currentOpacity + OpacityStep);
        EdgeLightBorder.Opacity = currentOpacity;
    }

    public void DecreaseBrightness()
    {
        currentOpacity = Math.Max(MinOpacity, currentOpacity - OpacityStep);
        EdgeLightBorder.Opacity = currentOpacity;
    }

    public void MoveToNextMonitor()
    {
        // Refresh monitor list in case of hot-plug/unplug
        availableMonitors = Screen.AllScreens;

        if (availableMonitors.Length <= 1)
        {
            // Only one monitor, nothing to do
            return;
        }

        // Bounds check: if current monitor no longer exists, reset to primary
        if (currentMonitorIndex >= availableMonitors.Length)
        {
            // Find primary monitor again
            currentMonitorIndex = 0;
            for (int i = 0; i < availableMonitors.Length; i++)
            {
                if (availableMonitors[i].Primary)
                {
                    currentMonitorIndex = i;
                    break;
                }
            }
        }

        // Cycle to next monitor
        currentMonitorIndex = (currentMonitorIndex + 1) % availableMonitors.Length;
        var targetScreen = availableMonitors[currentMonitorIndex];

        // Reposition main window to new monitor
        SetupWindowForScreen(targetScreen);
        
        // Recreate the frame geometry for new dimensions
        CreateFrameGeometry();
        
        // Reposition control window to follow
        RepositionControlWindow();
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
        // Refresh monitor list
        availableMonitors = Screen.AllScreens;
        
        // Figure out which monitor we're actually on now
        var windowCenter = new System.Drawing.Point(
            (int)(this.Left + this.Width / 2),
            (int)(this.Top + this.Height / 2)
        );
        
        for (int i = 0; i < availableMonitors.Length; i++)
        {
            if (availableMonitors[i].Bounds.Contains(windowCenter))
            {
                currentMonitorIndex = i;
                break;
            }
        }
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