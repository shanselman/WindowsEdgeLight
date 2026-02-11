using System.Windows.Forms;
using WindowsEdgeLight.ViewModels;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Manages the system tray notification icon and context menu.
/// </summary>
public class TrayIconManager
{
    private readonly MainViewModel _viewModel;
    private readonly Action<MainWindow> _mainWindowRef;
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _toggleControlsMenuItem;
    private ToolStripMenuItem? _excludeFromCaptureMenuItem;

    public TrayIconManager(MainViewModel viewModel, Action<MainWindow> mainWindowRef)
    {
        _viewModel = viewModel;
        _mainWindowRef = mainWindowRef;
    }

    /// <summary>
    /// Sets up the system tray icon and context menu.
    /// </summary>
    public void Setup(MainWindow mainWindow, AppSettings settings, Action showHelpAction, Action toggleExcludeAction)
    {
        _notifyIcon = new NotifyIcon();
        
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "ringlight_cropped.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                var appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
                _notifyIcon.Icon = appIcon ?? System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }
        
        _notifyIcon.Text = "Windows Edge Light - Right-click for options";
        _notifyIcon.Visible = true;
        
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("📋 Keyboard Shortcuts", null, (s, e) => _viewModel.ShowHelpCommand.Execute(null));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("💡 Toggle Light (Ctrl+Shift+L)", null, (s, e) => _viewModel.ToggleLightCommand.Execute(null));
        contextMenu.Items.Add("🔆 Brightness Up (Ctrl+Shift+↑)", null, (s, e) => _viewModel.IncreaseBrightnessCommand.Execute(null));
        contextMenu.Items.Add("🔅 Brightness Down (Ctrl+Shift+↓)", null, (s, e) => _viewModel.DecreaseBrightnessCommand.Execute(null));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("🔥 K- Warmer Light", null, (s, e) => _viewModel.IncreaseColorTemperatureCommand.Execute(null));
        contextMenu.Items.Add("❄️ K+ Cooler Light", null, (s, e) => _viewModel.DecreaseColorTemperatureCommand.Execute(null));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("🖥️ Switch Monitor", null, (s, e) => _viewModel.MoveToNextMonitorCommand.Execute(null));
        contextMenu.Items.Add("🖥️🖥️ Toggle All Monitors", null, (s, e) => _viewModel.ToggleAllMonitorsCommand.Execute(null));
        contextMenu.Items.Add(new ToolStripSeparator());
        
        _toggleControlsMenuItem = new ToolStripMenuItem("🎛️ Hide Controls", null, (s, e) => _viewModel.ToggleControlsVisibilityCommand.Execute(null));
        contextMenu.Items.Add(_toggleControlsMenuItem);
        
        _excludeFromCaptureMenuItem = new ToolStripMenuItem("🎥 Exclude from Screen Capture", null, (s, e) => toggleExcludeAction());
        _excludeFromCaptureMenuItem.CheckOnClick = true;
        _excludeFromCaptureMenuItem.Checked = settings.ExcludeFromCapture;
        contextMenu.Items.Add(_excludeFromCaptureMenuItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("✖ Exit", null, (s, e) => _viewModel.ExitCommand.Execute(null));
        
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => showHelpAction();
        
        UpdateToggleControlsText();
    }

    /// <summary>
    /// Updates the toggle controls menu item text based on current state.
    /// </summary>
    public void UpdateToggleControlsText()
    {
        if (_toggleControlsMenuItem != null)
        {
            _toggleControlsMenuItem.Text = _viewModel.IsControlWindowVisible ? "🎛️ Hide Controls" : "🎛️ Show Controls";
        }
    }

    /// <summary>
    /// Updates the exclude from capture menu item state.
    /// </summary>
    public void UpdateExcludeFromCaptureState(bool isExcluded)
    {
        if (_excludeFromCaptureMenuItem != null)
        {
            _excludeFromCaptureMenuItem.Checked = isExcluded;
        }
    }

    /// <summary>
    /// Cleans up the tray icon.
    /// </summary>
    public void Cleanup()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
