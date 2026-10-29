using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

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

    // Widened from the original 76: on lower-DPI monitors this DIP value maps to fewer
    // physical pixels (confirmed via alpha-channel sampling: ~30px transition at 1.5x DPI
    // vs ~20px at 1.0x DPI - both are the same size in DIPs, DPI scaling is working
    // correctly, but 20 raw pixels is little enough for some display paths - certain
    // adapters/monitors reducing bit depth or applying chroma subsampling, or a monitor's
    // own sharpening - to crush into a hard edge instead of a gradient. A wider transition
    // survives that kind of degradation better everywhere, not just on affected monitors.
    // This only costs anything on genuine content changes - color temperature,
    // resize/monitor-switch, toggle - via RenderGlow's WPF off-screen pipeline. Brightness
    // never touches it (SetBrightness is a pure native alpha blend) and hover no longer
    // does either (FlushHoverRender punches the hole into a cached bitmap instead of
    // re-rendering), so a larger radius no longer costs anything on the two interactions
    // that were actually causing the CPU spikes.
    private const double BlurRadius = 140;

    // Widened alongside BlurRadius so the larger blur has room to bleed inward without
    // being clipped at the window edge (35px each side, was 20px).
    private const double MarginInset = 70;

    // DPI Scale
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    private bool _isManualMonitorSwitch = false;

    private NotifyIcon? notifyIcon;
    private ControlWindow? controlWindow;
    // Tracks whether the control window should be visible (controls initial visibility and toggle state)
    private bool isControlWindowVisible = true;
    // Session-only: once the user drags the control toolbar, stop auto-repositioning it.
    private bool controlWindowManuallyMoved = false;
    private ToolStripMenuItem? toggleControlsMenuItem;
    private ToolStripMenuItem? excludeFromCaptureMenuItem;
    private ToolStripMenuItem? toggleLightMenuItem;

    // Application settings
    private AppSettings settings = new AppSettings();

    // Per-monitor glow window state. The glow itself is rendered by a NativeLayeredWindow
    // (a raw Win32 layered window, not a WPF window - see NativeLayeredWindow.cs for why),
    // built off-screen by GlowBitmapRenderer. This class only tracks the geometry/state
    // needed to know when and how to re-render or reposition it.
    private sealed class GlowWindowContext
    {
        public NativeLayeredWindow NativeWindow { get; set; } = null!;
        public Screen Screen { get; set; } = null!;
        public Rect FrameOuterRect { get; set; }
        public Rect FrameInnerRect { get; set; }
        public double PathOffsetX { get; set; }
        public double PathOffsetY { get; set; }
        public double DpiScaleX { get; set; } = 1.0;
        public double DpiScaleY { get; set; } = 1.0;
        public double DipWidth { get; set; }
        public double DipHeight { get; set; }
        // Hole-punch center in geometry-local coordinates, or null when not hovering the frame.
        public System.Windows.Point? HoleCenter { get; set; }

        // Hover-triggered re-renders are throttled per context (see RequestHoverRender) -
        // unlike brightness (free) or color-temp (throttled once already in the Settings
        // UI), hover was left uncapped and could re-render as fast as the mouse reports
        // move events, pinning a CPU core while hovering the frame.
        public DateTime LastHoverRenderAt { get; set; } = DateTime.MinValue;
        public DispatcherTimer? HoverFlushTimer { get; set; }
        public System.Windows.Point? PendingHoleCenter { get; set; }
        public bool HasPendingHoverRender { get; set; }

        // The frame rendered WITHOUT a hole, cached as raw premultiplied-BGRA32 pixel
        // bytes. Hover updates punch a hole directly into a copy of this (see
        // PushCurrentState/PunchHoleInPixels) instead of re-running WPF's off-screen
        // render pipeline - measured at 100-300ms per call depending on monitor DPI/size,
        // far too slow to repeat on every hover tick. Only regenerated by RenderGlow,
        // which runs on genuine content changes (color, geometry) - infrequent.
        public byte[]? BaseFramePixels { get; set; }
        public byte[]? ScratchPixels { get; set; }
        public int BaseFrameWidth { get; set; }
        public int BaseFrameHeight { get; set; }
        public int BaseFrameStride { get; set; }

        // Incremented on every RenderGlow call for this context. RenderGlowAsync captures
        // the value at request time and checks it again before applying its result, so a
        // slow render superseded by a newer one (e.g. during a fast color-temp drag) gets
        // discarded instead of flickering the frame back to a stale color.
        public int RenderRequestId { get; set; }

        // Set to the RenderRequestId that was actually applied. RenderRequestId != this
        // means a requested render hasn't landed yet - used to drive the "Applying..."
        // indicator in the Settings window.
        public int AppliedRequestId { get; set; }
    }

    private GlowWindowContext? _primaryGlow;
    private readonly List<GlowWindowContext> _additionalGlows = new();
    private readonly GlowRenderWorker _renderWorker = new();

    // Monitor management
    private int currentMonitorIndex = 0;
    private Screen[] availableMonitors = Array.Empty<Screen>();
    private bool showOnAllMonitors = false;

    // Global hotkey IDs
    private const int HOTKEY_TOGGLE = 1;
    private const int HOTKEY_BRIGHTNESS_UP = 2;
    private const int HOTKEY_BRIGHTNESS_DOWN = 3;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

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

    // Mouse-move coalescing: only the latest position is kept, and only one
    // Dispatcher callback is ever outstanding at a time, regardless of how
    // many raw WM_MOUSEMOVE hook events arrive while it's pending.
    private bool _mouseUpdateQueued = false;
    private bool _wasNearFrame = false;
    private int _pendingMouseX;
    private int _pendingMouseY;

    private static readonly MediaColor CoolColor = MediaColor.FromRgb(220, 235, 255);
    private static readonly MediaColor WarmColor = MediaColor.FromRgb(255, 220, 180);

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_L = 0x4C;
    private const uint VK_UP = 0x26;
    private const uint VK_DOWN = 0x28;

    public MainWindow()
    {
        InitializeComponent();

        // Load settings
        settings = AppSettings.Load();
        isLightOn = settings.IsLightOn;
        currentOpacity = settings.Brightness;
        _colorTemperature = settings.ColorTemperature;

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

        notifyIcon.Text = GetTrayTooltipText();
        notifyIcon.Visible = true;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("📋 Keyboard Shortcuts", null, (s, e) => ShowHelp());
        contextMenu.Items.Add(new ToolStripSeparator());
        toggleLightMenuItem = new ToolStripMenuItem(GetToggleLightMenuText(), null, (s, e) => ToggleLight());
        contextMenu.Items.Add(toggleLightMenuItem);
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
        contextMenu.Items.Add("📍 Reset Control Bar Position", null, (s, e) => ResetControlWindowPosition());

        // Add exclude from capture menu item with checkmark
        excludeFromCaptureMenuItem = new ToolStripMenuItem("🎥 Exclude from Screen Capture", null, (s, e) => ToggleExcludeFromCapture());
        excludeFromCaptureMenuItem.CheckOnClick = true;
        excludeFromCaptureMenuItem.Checked = settings.ExcludeFromCapture;
        contextMenu.Items.Add(excludeFromCaptureMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("✖ Exit", null, (s, e) => System.Windows.Application.Current.Shutdown());

        notifyIcon.ContextMenuStrip = contextMenu;
        notifyIcon.DoubleClick += (s, e) => ShowHelp();

        // Set initial menu text based on current state
        UpdateTrayLightStateText();
        UpdateTrayMenuToggleControlsText();
    }

    private string GetTrayTooltipText()
    {
        return isLightOn
            ? "Windows Edge Light - Light ON (right-click for options)"
            : "Windows Edge Light - Light OFF (right-click for options)";
    }

    private string GetToggleLightMenuText()
    {
        return isLightOn
            ? "💡 Turn Light Off (Ctrl+Shift+L)"
            : "💡 Turn Light On (Ctrl+Shift+L)";
    }

    private void UpdateTrayLightStateText()
    {
        if (notifyIcon != null)
        {
            notifyIcon.Text = GetTrayTooltipText();
        }

        if (toggleLightMenuItem != null)
        {
            toggleLightMenuItem.Text = GetToggleLightMenuText();
        }
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
• Exclude from screen capture (🎥) - invisible in Teams/Zoom sharing

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
            RefreshAvailableMonitors();

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

        var targetScreen = GetCurrentScreen();
        if (targetScreen == null) return;

        SetupWindowForScreen(targetScreen);
    }

    private void SetupWindowForScreen(Screen screen)
    {
        // Get DPI scale factor for the target screen
        (_dpiScaleX, _dpiScaleY) = GetDpiForScreen(screen);

        PositionMainWindowShell(screen, _dpiScaleX, _dpiScaleY);
        this.WindowState = System.Windows.WindowState.Normal;
    }

    // Pins this window to a 1x1px point at the target monitor's origin, in DIPs. This
    // window must stay tiny and off to the side of real content - see the AllowsTransparency
    // removal note in MainWindow.xaml for why it can no longer cover the whole monitor.
    // It still needs to sit ON the correct monitor (not literally anywhere) so DPI-change
    // detection (WM_DPICHANGED) keeps firing correctly when the user switches monitors.
    private void PositionMainWindowShell(Screen screen, double dpiScaleX, double dpiScaleY)
    {
        var workingArea = screen.WorkingArea;
        this.Left = workingArea.X / dpiScaleX;
        this.Top = workingArea.Y / dpiScaleY;
        this.Width = 1;
        this.Height = 1;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();

        var hwnd = new WindowInteropHelper(this).Handle;

        RegisterGlobalHotKeys(hwnd);

        // Hook into Windows message processing
        HwndSource source = HwndSource.FromHwnd(hwnd);
        source.AddHook(HwndHook);

        EnsurePrimaryGlowWindow();
        UpdatePrimaryGlowLayout();
        CreateControlWindow();

        // Listen for window size/location changes (docking/undocking)
        this.SizeChanged += Window_SizeChanged;
        this.LocationChanged += Window_LocationChanged;

        // Listen for OS-level display configuration changes (monitor connected/
        // disconnected, resolution/DPI changed) - see OnDisplaySettingsChanged for why
        // this is needed independently of SizeChanged/LocationChanged/OnDpiChanged, which
        // only fire when THIS window's own position/size/DPI changes, not when some OTHER
        // monitor's configuration changes while this window stays put.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Apply exclude from capture setting
        ApplyExcludeFromCapture();

        InstallMouseHook();
    }

    // SystemEvents fires on its own internal thread, not necessarily this window's UI
    // thread, so marshal onto the Dispatcher before touching anything WPF-affiliated.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshAvailableMonitors();
            UpdateCurrentMonitorIndex();
            UpdatePrimaryGlowLayout();
            RepositionControlWindow();

            // Monitors may have been added, removed, or resized - the safest way to keep
            // every additional-monitor glow window correct is to recreate them all against
            // the current monitor list, same as ShowOnAllMonitors already does when first
            // turning this mode on.
            if (showOnAllMonitors)
            {
                ShowOnAllMonitors();
            }
        }));
    }

    private void RegisterGlobalHotKeys(IntPtr hwnd)
    {
        var failedHotKeys = new List<string>();

        if (!RegisterHotKey(hwnd, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_L))
        {
            failedHotKeys.Add("Toggle Light (Ctrl+Shift+L)");
        }

        if (!RegisterHotKey(hwnd, HOTKEY_BRIGHTNESS_UP, MOD_CONTROL | MOD_SHIFT, VK_UP))
        {
            failedHotKeys.Add("Brightness Up (Ctrl+Shift+Up)");
        }

        if (!RegisterHotKey(hwnd, HOTKEY_BRIGHTNESS_DOWN, MOD_CONTROL | MOD_SHIFT, VK_DOWN))
        {
            failedHotKeys.Add("Brightness Down (Ctrl+Shift+Down)");
        }

        if (failedHotKeys.Count > 0)
        {
            notifyIcon?.ShowBalloonTip(
                5000,
                "Windows Edge Light hotkey conflict",
                "Some keyboard shortcuts could not be registered because another app is using them:\n\n" +
                string.Join("\n", failedHotKeys) +
                "\n\nUse the tray menu controls instead.",
                ToolTipIcon.Warning);
        }
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
            int x = hookStruct.pt.x;
            int y = hookStruct.pt.y;

            // Cheap arithmetic-only check (no WPF object access) so we never touch the
            // dispatcher for the vast majority of system-wide mouse moves that have nothing
            // to do with our overlay. We still need one more pass through when the cursor
            // *leaves* the frame band, so a hole-punch left behind gets cleared.
            bool isNearFrame = IsPointRelevant(x, y);

            if (isNearFrame || _wasNearFrame)
            {
                _wasNearFrame = isNearFrame;
                _pendingMouseX = x;
                _pendingMouseY = y;

                // Coalesce: if a callback is already queued, just update the pending
                // position and let that callback pick up the latest value when it runs -
                // don't pile up a redundant BeginInvoke per raw hook event.
                if (!_mouseUpdateQueued)
                {
                    _mouseUpdateQueued = true;
                    Dispatcher.BeginInvoke(new Action(ProcessPendingMouseMove), DispatcherPriority.Input);
                }
            }
        }

        return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
    }

    private void ProcessPendingMouseMove()
    {
        _mouseUpdateQueued = false;
        HandleMouseMove(_pendingMouseX, _pendingMouseY);
    }

    // True if (screenX, screenY) is within the hover-detectable frame band of the primary
    // glow window or any additional monitor's. Pure arithmetic on cached Rects/doubles -
    // no rendering - so it's safe to call directly from the mouse hook callback.
    private bool IsPointRelevant(int screenX, int screenY)
    {
        if (!isLightOn)
        {
            return false;
        }

        if (_primaryGlow != null)
        {
            var pt = ToWindowPoint(screenX, screenY, _primaryGlow.Screen, _primaryGlow.DpiScaleX, _primaryGlow.DpiScaleY);
            if (IsOverFrame(pt, _primaryGlow.FrameOuterRect, _primaryGlow.FrameInnerRect, GlowBitmapRenderer.HoleRadius))
            {
                return true;
            }
        }

        foreach (var ctx in _additionalGlows)
        {
            var pt = ToWindowPoint(screenX, screenY, ctx.Screen, ctx.DpiScaleX, ctx.DpiScaleY);
            if (IsOverFrame(pt, ctx.FrameOuterRect, ctx.FrameInnerRect, GlowBitmapRenderer.HoleRadius))
            {
                return true;
            }
        }

        return false;
    }

    private static System.Windows.Point ToWindowPoint(int screenX, int screenY, Screen screen, double dpiScaleX, double dpiScaleY)
    {
        // Manual coordinate calculation to avoid PointFromScreen issues across monitors/DPIs.
        double relX = (screenX - screen.WorkingArea.X) / dpiScaleX;
        double relY = (screenY - screen.WorkingArea.Y) / dpiScaleY;
        return new System.Windows.Point(relX, relY);
    }

    private static bool IsOverFrame(System.Windows.Point windowPt, Rect frameOuterRect, Rect frameInnerRect, double holeRadius)
    {
        // Existing frame band detection (outer minus inner)
        bool inFrameBand = frameOuterRect.Contains(windowPt) && !frameInnerRect.Contains(windowPt);

        // Early detection zone just inside the inner edge: a band with thickness = hole radius
        var innerProximityRect = new Rect(
            frameInnerRect.X + holeRadius,
            frameInnerRect.Y + holeRadius,
            frameInnerRect.Width - (holeRadius * 2),
            frameInnerRect.Height - (holeRadius * 2));

        bool nearFromInside = frameInnerRect.Contains(windowPt) && !innerProximityRect.Contains(windowPt);

        return inFrameBand || nearFromInside;
    }

    private static double ClampFinite(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static byte BrightnessToAlpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255);

    private void HandleMouseMove(int screenX, int screenY)
    {
        if (!isLightOn) return;

        if (_primaryGlow != null)
        {
            UpdateHoverForContext(_primaryGlow, screenX, screenY);
        }

        foreach (var ctx in _additionalGlows)
        {
            UpdateHoverForContext(ctx, screenX, screenY);
        }
    }

    // Hover re-renders are capped to this rate per context - well above what's visually
    // perceptible for a cursor-following hole, but far below "every raw mouse move".
    private static readonly TimeSpan HoverRenderInterval = TimeSpan.FromMilliseconds(33);

    private void UpdateHoverForContext(GlowWindowContext ctx, int screenX, int screenY)
    {
        var windowPt = ToWindowPoint(screenX, screenY, ctx.Screen, ctx.DpiScaleX, ctx.DpiScaleY);
        bool overFrame = IsOverFrame(windowPt, ctx.FrameOuterRect, ctx.FrameInnerRect, GlowBitmapRenderer.HoleRadius);

        System.Windows.Point? newHoleCenter = overFrame
            ? new System.Windows.Point(windowPt.X - ctx.PathOffsetX, windowPt.Y - ctx.PathOffsetY)
            : null;

        // Compare against whatever was most recently requested (pending if there is one,
        // otherwise the last applied value) so a still-throttled burst of identical
        // requests doesn't keep rescheduling redundant work.
        var effectiveCurrent = ctx.HasPendingHoverRender ? ctx.PendingHoleCenter : ctx.HoleCenter;
        if (newHoleCenter == effectiveCurrent) return;

        RequestHoverRender(ctx, newHoleCenter);
    }

    private void RequestHoverRender(GlowWindowContext ctx, System.Windows.Point? newHoleCenter)
    {
        ctx.PendingHoleCenter = newHoleCenter;
        ctx.HasPendingHoverRender = true;

        if (DateTime.UtcNow - ctx.LastHoverRenderAt >= HoverRenderInterval)
        {
            FlushHoverRender(ctx);
            return;
        }

        if (ctx.HoverFlushTimer == null)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = HoverRenderInterval };
            timer.Tick += (s, e) => FlushHoverRender(ctx);
            ctx.HoverFlushTimer = timer;
        }

        if (!ctx.HoverFlushTimer.IsEnabled)
        {
            ctx.HoverFlushTimer.Start();
        }
    }

    private void FlushHoverRender(GlowWindowContext ctx)
    {
        ctx.HoverFlushTimer?.Stop();
        if (!ctx.HasPendingHoverRender) return;

        ctx.HasPendingHoverRender = false;
        ctx.LastHoverRenderAt = DateTime.UtcNow;
        ctx.HoleCenter = ctx.PendingHoleCenter;
        // Fast path: punch the hole into the already-cached base frame instead of calling
        // RenderGlow, which would re-run WPF's full off-screen render (100-300ms) on every
        // hover tick - see PushCurrentState.
        PushCurrentState(ctx);
    }

    private void EnsurePrimaryGlowWindow()
    {
        _primaryGlow ??= new GlowWindowContext { NativeWindow = new NativeLayeredWindow() };
    }

    private void UpdatePrimaryGlowLayout()
    {
        var screen = GetCurrentScreen();
        if (screen == null || _primaryGlow == null) return;

        var ctx = _primaryGlow;
        ctx.Screen = screen;
        ctx.DpiScaleX = _dpiScaleX;
        ctx.DpiScaleY = _dpiScaleY;

        var workingArea = screen.WorkingArea;
        ctx.DipWidth = workingArea.Width / _dpiScaleX;
        ctx.DipHeight = workingArea.Height / _dpiScaleY;
        ctx.PathOffsetX = MarginInset / 2;
        ctx.PathOffsetY = MarginInset / 2;

        ComputeFrameRects(ctx);
        ctx.HoleCenter = null; // clear any stale hole from before a resize/monitor switch
        // Also drop any hover render still queued against the old geometry - letting it
        // fire later would overwrite this fresh layout with a stale hole position.
        ctx.HoverFlushTimer?.Stop();
        ctx.HasPendingHoverRender = false;

        ctx.NativeWindow.SetBounds(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height);
        RenderGlow(ctx);

        if (isLightOn) ctx.NativeWindow.Show(); else ctx.NativeWindow.Hide();
    }

    private static void ComputeFrameRects(GlowWindowContext ctx)
    {
        double insetWidth = ctx.DipWidth - MarginInset;
        double insetHeight = ctx.DipHeight - MarginInset;
        double holeRadius = GlowBitmapRenderer.HoleRadius;

        ctx.FrameOuterRect = new Rect(
            ctx.PathOffsetX - holeRadius, ctx.PathOffsetY - holeRadius,
            insetWidth + holeRadius * 2, insetHeight + holeRadius * 2);

        ctx.FrameInnerRect = new Rect(
            ctx.PathOffsetX + GlowBitmapRenderer.FrameThickness + holeRadius,
            ctx.PathOffsetY + GlowBitmapRenderer.FrameThickness + holeRadius,
            insetWidth - GlowBitmapRenderer.FrameThickness * 2 - holeRadius * 2,
            insetHeight - GlowBitmapRenderer.FrameThickness * 2 - holeRadius * 2);
    }

    // Kicks off a re-render of the context's BASE frame - always WITHOUT a hole,
    // regardless of whether one is currently active - on the background render worker
    // (see GlowRenderWorker for why: this is the expensive part, layout + the blur
    // Effect, measured at 100-300ms depending on monitor DPI/resolution, and running it
    // on the main thread was stalling the global mouse hook installed on that same
    // thread). Fire-and-forget: returns immediately, applies the result asynchronously
    // once it's ready (see RenderGlowAsync). Call this for genuine content changes only:
    // color temperature, geometry (resize/monitor switch), initial setup. NOT for hover
    // (see RequestHoverRender/FlushHoverRender, which reuse the cache this produces) and
    // NOT for brightness (see SetBrightness, which uses NativeLayeredWindow.SetAlpha()
    // and never touches pixels at all).
    private void RenderGlow(GlowWindowContext ctx)
    {
        double insetWidth = ctx.DipWidth - MarginInset;
        double insetHeight = ctx.DipHeight - MarginInset;
        var temperatureColor = GetColorForTemperature(_colorTemperature);
        int requestId = ++ctx.RenderRequestId;
        NotifyGlowRenderBusyChanged();

        _ = RenderGlowAsync(ctx, insetWidth, insetHeight, temperatureColor, requestId);
    }

    private async Task RenderGlowAsync(GlowWindowContext ctx, double insetWidth, double insetHeight, MediaColor color, int requestId)
    {
        RenderedFrame? frame;
        try
        {
            frame = await _renderWorker.RenderAsync(
                ctx.DipWidth, ctx.DipHeight, insetWidth, insetHeight,
                color, BlurRadius, ctx.DpiScaleX, ctx.DpiScaleY,
                () => ctx.RenderRequestId == requestId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RenderGlowAsync failed: {ex}");
            ctx.AppliedRequestId = requestId;
            NotifyGlowRenderBusyChanged();
            return;
        }

        // Null means the worker skipped the render because a newer request had already
        // superseded this one; a requestId mismatch means a newer one landed while this
        // one was in flight. Either way, applying it now would flicker the frame back to
        // stale content - but this request is still settled either way, so still notify.
        if (frame == null || ctx.RenderRequestId != requestId)
        {
            NotifyGlowRenderBusyChanged();
            return;
        }

        ctx.BaseFramePixels = frame.Value.Pixels;
        ctx.BaseFrameWidth = frame.Value.Width;
        ctx.BaseFrameHeight = frame.Value.Height;
        ctx.BaseFrameStride = frame.Value.Stride;
        ctx.AppliedRequestId = requestId;

        PushCurrentState(ctx);
        NotifyGlowRenderBusyChanged();
    }

    // Fires when IsGlowRenderBusy actually flips, so the Settings window can show/hide an
    // "Applying..." indicator next to the color-temperature slider while a change is
    // still rendering in the background (see GlowRenderWorker) instead of appearing to
    // just ignore the input for a few hundred milliseconds.
    public event EventHandler? GlowRenderBusyChanged;
    private bool _lastReportedGlowRenderBusy;

    public bool IsGlowRenderBusy =>
        (_primaryGlow != null && _primaryGlow.RenderRequestId != _primaryGlow.AppliedRequestId) ||
        _additionalGlows.Any(ctx => ctx.RenderRequestId != ctx.AppliedRequestId);

    private void NotifyGlowRenderBusyChanged()
    {
        bool current = IsGlowRenderBusy;
        if (current == _lastReportedGlowRenderBusy) return;
        _lastReportedGlowRenderBusy = current;
        GlowRenderBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    // Pushes the context's current state (base frame, or base + hole punched in if
    // currently hovering) from the cached pixel bytes - no WPF rendering involved, just a
    // bulk memory copy and (if hovering) a small circular pixel-fill over the hole's
    // bounding box. This is the fast path both RenderGlow and hover updates end at.
    private void PushCurrentState(GlowWindowContext ctx)
    {
        if (ctx.BaseFramePixels == null) return;

        byte alpha = BrightnessToAlpha(currentOpacity);

        if (ctx.HoleCenter is System.Windows.Point center)
        {
            if (ctx.ScratchPixels == null || ctx.ScratchPixels.Length != ctx.BaseFramePixels.Length)
            {
                ctx.ScratchPixels = new byte[ctx.BaseFramePixels.Length];
            }
            Buffer.BlockCopy(ctx.BaseFramePixels, 0, ctx.ScratchPixels, 0, ctx.BaseFramePixels.Length);

            // HoleCenter is in geometry-local DIPs (relative to the inset frame's own
            // origin) - convert to full-bitmap pixel coordinates: add back the centering
            // offset (PathOffsetX/Y), then scale DIPs to pixels.
            double centerXPx = (center.X + ctx.PathOffsetX) * ctx.DpiScaleX;
            double centerYPx = (center.Y + ctx.PathOffsetY) * ctx.DpiScaleY;
            double radiusPx = GlowBitmapRenderer.HoleRadius * ctx.DpiScaleX;
            double featherPx = HoleFeatherDip * ctx.DpiScaleX;

            PunchHoleInPixels(ctx.ScratchPixels, ctx.BaseFrameWidth, ctx.BaseFrameHeight, ctx.BaseFrameStride, centerXPx, centerYPx, radiusPx, featherPx);
            ctx.NativeWindow.RenderPixels(ctx.ScratchPixels, ctx.BaseFrameWidth, ctx.BaseFrameHeight, alpha);
        }
        else
        {
            ctx.NativeWindow.RenderPixels(ctx.BaseFramePixels, ctx.BaseFrameWidth, ctx.BaseFrameHeight, alpha);
        }
    }

    // Width of the soft transition around the hole's edge, in DIPs before DPI scaling.
    // The base frame is blurred (see BlurRadius), so its own edges are soft; without this,
    // the hole - cut directly into already-rendered pixels rather than into geometry
    // before blurring, as the original implementation did - would show a harshly crisp
    // circular edge against that softness. A feather roughly this wide reads as consistent
    // with the surrounding blur without needing to re-run it.
    private const double HoleFeatherDip = 14;

    // Fades every pixel within radiusPx + featherPx of the given center toward fully
    // transparent (premultiplied zero) - a hard cut inside radiusPx, a linear falloff
    // across the feather band outside it - restricted to that bounding box, not the whole
    // image, so this stays cheap (a few hundred pixels) regardless of the frame's overall
    // resolution.
    private static void PunchHoleInPixels(byte[] pixels, int width, int height, int stride, double centerXPx, double centerYPx, double radiusPx, double featherPx)
    {
        double outerRadius = radiusPx + featherPx;
        int minX = Math.Max(0, (int)Math.Floor(centerXPx - outerRadius));
        int maxX = Math.Min(width - 1, (int)Math.Ceiling(centerXPx + outerRadius));
        int minY = Math.Max(0, (int)Math.Floor(centerYPx - outerRadius));
        int maxY = Math.Min(height - 1, (int)Math.Ceiling(centerYPx + outerRadius));

        for (int y = minY; y <= maxY; y++)
        {
            double dy = y + 0.5 - centerYPx;
            int rowOffset = y * stride;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x + 0.5 - centerXPx;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist >= outerRadius) continue; // untouched

                int offset = rowOffset + x * 4;
                if (dist <= radiusPx || featherPx <= 0)
                {
                    pixels[offset] = 0;
                    pixels[offset + 1] = 0;
                    pixels[offset + 2] = 0;
                    pixels[offset + 3] = 0;
                }
                else
                {
                    // 0 at the hard edge (fully punched) rising to 1 at the outer edge
                    // (untouched) - scaling all four premultiplied channels by the same
                    // factor keeps premultiplication valid.
                    double keep = (dist - radiusPx) / featherPx;
                    pixels[offset] = (byte)(pixels[offset] * keep);
                    pixels[offset + 1] = (byte)(pixels[offset + 1] * keep);
                    pixels[offset + 2] = (byte)(pixels[offset + 2] * keep);
                    pixels[offset + 3] = (byte)(pixels[offset + 3] * keep);
                }
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

        // Re-verify which monitor we are on, as we might have just moved, and keep this
        // shell window pinned to that monitor's origin at the new DPI.
        if (availableMonitors.Length > 0)
        {
            UpdateCurrentMonitorIndex();

            if (currentMonitorIndex < availableMonitors.Length)
            {
                PositionMainWindowShell(availableMonitors[currentMonitorIndex], _dpiScaleX, _dpiScaleY);
            }
        }

        // The glow window's own geometry is DPI-dependent too - refresh it here rather
        // than relying solely on Window_SizeChanged, since this shell window's size no
        // longer changes with the monitor (see PositionMainWindowShell).
        UpdatePrimaryGlowLayout();
    }

    protected override void OnClosed(EventArgs e)
    {
        // SystemEvents subscriptions are static/global and outlive this window unless
        // explicitly removed - unlike normal instance events, they won't just get GC'd.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

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
        _primaryGlow?.HoverFlushTimer?.Stop();
        _primaryGlow?.NativeWindow.Dispose();
        _renderWorker.Dispose();
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

    private void ToggleLight()
    {
        isLightOn = !isLightOn;

        if (isLightOn)
        {
            _primaryGlow?.NativeWindow.Show();
            foreach (var ctx in _additionalGlows) ctx.NativeWindow.Show();
        }
        else
        {
            _primaryGlow?.NativeWindow.Hide();
            foreach (var ctx in _additionalGlows) ctx.NativeWindow.Hide();
        }

        settings.IsLightOn = isLightOn;
        settings.Save();
        UpdateTrayLightStateText();
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

    public void ToggleExcludeFromCapture()
    {
        settings.ExcludeFromCapture = !settings.ExcludeFromCapture;
        settings.Save();

        // Update menu checkmark
        if (excludeFromCaptureMenuItem != null)
        {
            excludeFromCaptureMenuItem.Checked = settings.ExcludeFromCapture;
        }

        // Apply the setting to all windows
        ApplyExcludeFromCapture();
    }

    private void ApplyExcludeFromCapture()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var result = SetWindowDisplayAffinity(hwnd, settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            if (!result)
            {
                var error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"Failed to set display affinity for main window. Error: {error}");
            }
        }

        if (_primaryGlow != null)
        {
            SetWindowDisplayAffinity(_primaryGlow.NativeWindow.Handle, settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }

        // Apply to control window
        if (controlWindow != null)
        {
            var controlHwnd = new WindowInteropHelper(controlWindow).Handle;
            if (controlHwnd != IntPtr.Zero)
            {
                var result = SetWindowDisplayAffinity(controlHwnd, settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
                if (!result)
                {
                    var error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"Failed to set display affinity for control window. Error: {error}");
                }
            }
        }

        // Apply to all additional monitor glow windows
        foreach (var ctx in _additionalGlows)
        {
            SetWindowDisplayAffinity(ctx.NativeWindow.Handle, settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }
    }

    public void IncreaseBrightness()
    {
        SetBrightness(currentOpacity + OpacityStep);
    }

    public void DecreaseBrightness()
    {
        SetBrightness(currentOpacity - OpacityStep);
    }

    // Brightness is applied as a native alpha blend via NativeLayeredWindow.SetAlpha() -
    // no re-render, no bitmap regeneration. This is the whole point of the rewrite: it's
    // just a GDI blit of the already-rendered bitmap with a new alpha value.
    public void SetBrightness(double value, bool save = true)
    {
        currentOpacity = ClampFinite(value, MinOpacity, MaxOpacity, MaxOpacity);
        byte alpha = BrightnessToAlpha(currentOpacity);

        _primaryGlow?.NativeWindow.SetAlpha(alpha);
        foreach (var ctx in _additionalGlows)
        {
            ctx.NativeWindow.SetAlpha(alpha);
        }

        if (save)
        {
            settings.Brightness = currentOpacity;
            settings.Save();
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

    public void SetColorTemperature(double value, bool save = true)
    {
        _colorTemperature = ClampFinite(value, MinColorTemp, MaxColorTemp, 0.5);

        if (_primaryGlow != null) RenderGlow(_primaryGlow);
        foreach (var ctx in _additionalGlows) RenderGlow(ctx);

        if (save)
        {
            settings.ColorTemperature = _colorTemperature;
            settings.Save();
        }
    }

    public void MoveToNextMonitor()
    {
        // If in all monitors mode, do nothing
        if (showOnAllMonitors) return;

        // Refresh monitor list in case of hot-plug/unplug
        RefreshAvailableMonitors();

        if (availableMonitors.Length <= 1)
        {
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

            // Reposition this shell window's hwnd to the new monitor using physical
            // coordinates, to trigger DPI change detection correctly. Keep it pinned to
            // 1x1px - see PositionMainWindowShell for why it must never cover real screen area.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, IntPtr.Zero,
                targetScreen.WorkingArea.X, targetScreen.WorkingArea.Y,
                1, 1,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // If DPI changed, OnDpiChanged will have already updated _dpiScaleX/Y and
            // repositioned this window synchronously during SetWindowPos (same thread).
            // Otherwise (same DPI, different monitor) do it explicitly here.
            PositionMainWindowShell(targetScreen, _dpiScaleX, _dpiScaleY);
            UpdatePrimaryGlowLayout();
        }
        finally
        {
            _isManualMonitorSwitch = false;
        }

        // An explicit monitor switch should bring the controls to the selected monitor.
        ResetControlWindowPosition();
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
        RefreshAvailableMonitors();

        // Close any existing additional windows
        HideAdditionalMonitorWindows();

        // Create a glow window for each monitor except the current one
        for (int i = 0; i < availableMonitors.Length; i++)
        {
            if (i != currentMonitorIndex)
            {
                var ctx = CreateMonitorGlow(availableMonitors[i]);
                _additionalGlows.Add(ctx);
                if (isLightOn)
                {
                    ctx.NativeWindow.Show();
                }
            }
        }
    }

    private void HideAdditionalMonitorWindows()
    {
        foreach (var ctx in _additionalGlows)
        {
            ctx.HoverFlushTimer?.Stop();
            ctx.NativeWindow.Dispose();
        }
        _additionalGlows.Clear();
    }

    private GlowWindowContext CreateMonitorGlow(Screen screen)
    {
        var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);
        var workingArea = screen.WorkingArea;

        var ctx = new GlowWindowContext
        {
            NativeWindow = new NativeLayeredWindow(),
            Screen = screen,
            DpiScaleX = screenDpiX,
            DpiScaleY = screenDpiY,
            DipWidth = workingArea.Width / screenDpiX,
            DipHeight = workingArea.Height / screenDpiY,
            PathOffsetX = MarginInset / 2,
            PathOffsetY = MarginInset / 2
        };

        ComputeFrameRects(ctx);

        ctx.NativeWindow.SetBounds(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height);
        RenderGlow(ctx);

        var result = SetWindowDisplayAffinity(ctx.NativeWindow.Handle, settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        if (!result)
        {
            var error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"Failed to set display affinity for monitor glow window. Error: {error}");
        }

        return ctx;
    }

    public bool IsShowingOnAllMonitors()
    {
        return showOnAllMonitors;
    }

    private void RepositionControlWindow()
    {
        if (controlWindow == null) return;

        // Respect a user-dragged position for the current session.
        if (controlWindowManuallyMoved) return;

        // This window itself is now pinned to 1x1px (see PositionMainWindowShell), so the
        // monitor rectangle has to come from the current screen directly rather than from
        // this.Left/Top/Width/Height as it used to.
        var screen = GetCurrentScreen();
        if (screen == null) return;

        var workingArea = screen.WorkingArea;
        double left = workingArea.X / _dpiScaleX;
        double top = workingArea.Y / _dpiScaleY;
        double width = workingArea.Width / _dpiScaleX;
        double height = workingArea.Height / _dpiScaleY;

        // Position at bottom center of the monitor
        controlWindow.Left = left + (width - controlWindow.Width) / 2;
        controlWindow.Top = top + height - controlWindow.Height - 124;
    }

    public void NotifyControlWindowManuallyMoved()
    {
        controlWindowManuallyMoved = true;
    }

    public void ResetControlWindowPosition()
    {
        controlWindowManuallyMoved = false;
        RepositionControlWindow();
    }

    public bool HasMultipleMonitors()
    {
        // Refresh monitor count to handle hot-plug scenarios
        RefreshAvailableMonitors();
        return availableMonitors.Length > 1;
    }

    public bool IsExcludeFromCaptureEnabled()
    {
        return settings.ExcludeFromCapture;
    }

    public double GetBrightness() => currentOpacity;

    public double GetColorTemperature() => _colorTemperature;

    public void SaveAppearanceSettings()
    {
        settings.Brightness = currentOpacity;
        settings.ColorTemperature = _colorTemperature;
        settings.Save();
    }

    public bool GetIsToggleButtonVisible() => settings.ShowToggleButton;

    public bool GetIsBrightnessButtonsVisible() => settings.ShowBrightnessButtons;

    public bool GetIsColorTempButtonsVisible() => settings.ShowColorTempButtons;

    public bool GetIsControlMonitorsButtonVisible() => settings.ShowMonitorControlButtons;

    public void SetIsToggleVisible(bool isVisible)
    {
        settings.ShowToggleButton = isVisible;
        settings.Save();
    }

    public void SetIsBrightnessButtonsVisible(bool isVisible)
    {
        settings.ShowBrightnessButtons = isVisible;
        settings.Save();
    }

    public void SetIsColorTempButtonsVisible(bool isVisible)
    {
        settings.ShowColorTempButtons = isVisible;
        settings.Save();
    }

    public void SetIsControlMonitorsButtonVisible(bool isVisible)
    {
        settings.ShowMonitorControlButtons = isVisible;
        settings.Save();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Recreate/reposition the glow when window size changes (e.g., different monitor resolution)
        UpdatePrimaryGlowLayout();

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

        RefreshAvailableMonitors();

        if (availableMonitors.Length == 0) return;

        try
        {
            // This window is pinned to 1x1px (see PositionMainWindowShell), so its "center"
            // is no longer meaningful for finding which monitor it's on - ask the OS which
            // monitor contains most of this hwnd's rect instead, which works at any size.
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var currentScreen = Screen.FromHandle(hwnd);

            for (int i = 0; i < availableMonitors.Length; i++)
            {
                if (availableMonitors[i].DeviceName == currentScreen.DeviceName)
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

    private void RefreshAvailableMonitors()
    {
        availableMonitors = Screen.AllScreens;
        currentMonitorIndex = availableMonitors.Length == 0
            ? 0
            : Math.Clamp(currentMonitorIndex, 0, availableMonitors.Length - 1);
    }

    private Screen? GetCurrentScreen()
    {
        if (availableMonitors.Length == 0)
        {
            return Screen.PrimaryScreen;
        }

        currentMonitorIndex = Math.Clamp(currentMonitorIndex, 0, availableMonitors.Length - 1);
        return availableMonitors[currentMonitorIndex];
    }

    private static MediaColor GetColorForTemperature(double temperature)
    {
        byte Lerp(byte cool, byte warm) => (byte)(cool + ((warm - cool) * temperature));
        return MediaColor.FromRgb(
            Lerp(CoolColor.R, WarmColor.R),
            Lerp(CoolColor.G, WarmColor.G),
            Lerp(CoolColor.B, WarmColor.B));
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
}
