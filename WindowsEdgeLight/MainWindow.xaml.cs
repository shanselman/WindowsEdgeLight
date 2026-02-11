using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WindowsEdgeLight.ViewModels;
using WindowsEdgeLight.Models;
using WindowsEdgeLight.Services;

namespace WindowsEdgeLight;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly MonitorManager _monitorManager;
    private readonly TrayIconManager _trayIconManager;
    private readonly FrameGeometryManager _frameGeometryManager;
    private readonly WindowPositioningManager _windowPositioningManager;
    
    private ControlWindow? _controlWindow;
    private AppSettings _settings = new AppSettings();

    // Monitor state
    private int _currentMonitorIndex = 0;
    private Screen[] _availableMonitors = Array.Empty<Screen>();

    // Global hotkey IDs
    private const int HOTKEY_TOGGLE = 1;
    private const int HOTKEY_BRIGHTNESS_UP = 2;
    private const int HOTKEY_BRIGHTNESS_DOWN = 3;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_L = 0x4C;
    private const uint VK_UP = 0x26;
    private const uint VK_DOWN = 0x28;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize ViewModel
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        
        // Initialize managers
        _monitorManager = new MonitorManager(_viewModel, _settings);
        _trayIconManager = new TrayIconManager(_viewModel, _ => { });
        _frameGeometryManager = new FrameGeometryManager(this);
        _windowPositioningManager = new WindowPositioningManager(this);
        
        // Subscribe to ViewModel events
        _viewModel.OnMoveToNextMonitor += MoveToNextMonitor;
        _viewModel.OnToggleAllMonitors += ToggleAllMonitors;
        _viewModel.OnShowHelp += ShowHelp;
        _viewModel.OnExit += () => System.Windows.Application.Current.Shutdown();
        
        // Subscribe to property changes
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        // Load settings
        _settings = AppSettings.Load();
        
        // Setup UI
        _trayIconManager.Setup(this, _settings, ShowHelp, ToggleExcludeFromCapture);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Update additional monitor windows when properties change
        if (e.PropertyName == nameof(MainViewModel.IsLightOn) ||
            e.PropertyName == nameof(MainViewModel.CurrentOpacity))
        {
            _monitorManager.UpdateAll();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsControlWindowVisible))
        {
            if (_controlWindow != null)
            {
                if (_viewModel.IsControlWindowVisible)
                    _controlWindow.Show();
                else
                    _controlWindow.Hide();
            }
            _trayIconManager.UpdateToggleControlsText();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _windowPositioningManager.SetupWindow(_availableMonitors, ref _currentMonitorIndex);
        _frameGeometryManager.CreateFrameGeometry();
        CreateControlWindow();
        
        var hwnd = new WindowInteropHelper(this).Handle;
        
        // Register global hotkeys
        RegisterHotKey(hwnd, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_L);
        RegisterHotKey(hwnd, HOTKEY_BRIGHTNESS_UP, MOD_CONTROL | MOD_SHIFT, VK_UP);
        RegisterHotKey(hwnd, HOTKEY_BRIGHTNESS_DOWN, MOD_CONTROL | MOD_SHIFT, VK_DOWN);
        
        // Setup message hook for hotkeys
        HwndSource source = HwndSource.FromHwnd(hwnd);
        source.AddHook(HwndHook);
        
        // Setup event listeners
        this.SizeChanged += Window_SizeChanged;
        this.LocationChanged += Window_LocationChanged;

        // Apply exclude from capture
        ApplyExcludeFromCapture();

        // Start mouse hook
        _frameGeometryManager.InstallMouseHook();
    }

    private void CreateControlWindow()
    {
        _controlWindow = new ControlWindow(this);
        RepositionControlWindow();
        
        // Only show if controls are supposed to be visible
        if (_viewModel.IsControlWindowVisible)
        {
            _controlWindow.Show();
        }
    }

    private void RepositionControlWindow()
    {
        if (_controlWindow == null) return;

        _controlWindow.Left = this.Left + (this.Width - _controlWindow.Width) / 2;
        _controlWindow.Top = this.Top + this.Height - _controlWindow.Height - 124;
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
                    _viewModel.ToggleLightCommand.Execute(null);
                    handled = true;
                    break;
                case HOTKEY_BRIGHTNESS_UP:
                    _viewModel.IncreaseBrightnessCommand.Execute(null);
                    handled = true;
                    break;
                case HOTKEY_BRIGHTNESS_DOWN:
                    _viewModel.DecreaseBrightnessCommand.Execute(null);
                    handled = true;
                    break;
            }
        }
        
        return IntPtr.Zero;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _windowPositioningManager.HandleDpiChange(newDpi, _availableMonitors, ref _currentMonitorIndex);
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameGeometryManager.UninstallMouseHook();
        
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HOTKEY_TOGGLE);
        UnregisterHotKey(hwnd, HOTKEY_BRIGHTNESS_UP);
        UnregisterHotKey(hwnd, HOTKEY_BRIGHTNESS_DOWN);
        
        _trayIconManager.Cleanup();
        _monitorManager.HideAllMonitors();
        _controlWindow?.Close();
        
        base.OnClosed(e);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.L && 
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && 
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            _viewModel.ToggleLightCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            System.Windows.Application.Current.Shutdown();
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

    public void ToggleExcludeFromCapture()
    {
        _settings.ExcludeFromCapture = !_settings.ExcludeFromCapture;
        _settings.Save();
        
        _trayIconManager.UpdateExcludeFromCaptureState(_settings.ExcludeFromCapture);
        ApplyExcludeFromCapture();
    }

    private void ApplyExcludeFromCapture()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var result = SetWindowDisplayAffinity(hwnd, _settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            if (!result)
            {
                var error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"Failed to set display affinity for main window. Error: {error}");
            }
        }
        
        if (_controlWindow != null)
        {
            var controlHwnd = new WindowInteropHelper(_controlWindow).Handle;
            if (controlHwnd != IntPtr.Zero)
            {
                var result = SetWindowDisplayAffinity(controlHwnd, _settings.ExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
                if (!result)
                {
                    var error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"Failed to set display affinity for control window. Error: {error}");
                }
            }
        }
    }

    private void MoveToNextMonitor()
    {
        if (_viewModel.ShowOnAllMonitors) return;
        
        _availableMonitors = Screen.AllScreens;

        if (_availableMonitors.Length <= 1) return;

        _windowPositioningManager.UpdateCurrentMonitorIndex(_availableMonitors, ref _currentMonitorIndex);

        try
        {
            _currentMonitorIndex = (_currentMonitorIndex + 1) % _availableMonitors.Length;
            var targetScreen = _availableMonitors[_currentMonitorIndex];

            _windowPositioningManager.PositionWindowOnScreen(this, targetScreen);
        }
        finally
        {
        }
        
        RepositionControlWindow();
    }

    private void ToggleAllMonitors()
    {
        _viewModel.ShowOnAllMonitors = !_viewModel.ShowOnAllMonitors;
        
        if (_viewModel.ShowOnAllMonitors)
        {
            _availableMonitors = Screen.AllScreens;
            _monitorManager.ShowOnAllMonitors(_availableMonitors, _currentMonitorIndex);
        }
        else
        {
            _monitorManager.HideAllMonitors();
        }

        _controlWindow?.UpdateAllMonitorsButtonState();
    }

    public bool HasMultipleMonitors()
    {
        _availableMonitors = Screen.AllScreens;
        return _availableMonitors.Length > 1;
    }

    public bool IsShowingOnAllMonitors() => _viewModel.ShowOnAllMonitors;

    public bool IsExcludeFromCaptureEnabled() => _settings.ExcludeFromCapture;

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FindName("EdgeLightBorder") is System.Windows.Shapes.Path edgeLightBorder)
        {
            _frameGeometryManager.CreateFrameGeometry();
        }
        
        RepositionControlWindow();
        _windowPositioningManager.UpdateCurrentMonitorIndex(_availableMonitors, ref _currentMonitorIndex);
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        RepositionControlWindow();
        _windowPositioningManager.UpdateCurrentMonitorIndex(_availableMonitors, ref _currentMonitorIndex);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
}
