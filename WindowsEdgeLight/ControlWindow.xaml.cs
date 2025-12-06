using System.Windows;

namespace WindowsEdgeLight;

public partial class ControlWindow : Window
{
    private readonly MainWindow mainWindow;

    public ControlWindow(MainWindow main)
    {
        InitializeComponent();
        mainWindow = main;
        
        // Disable switch monitor button if only one monitor
        UpdateMonitorButtonState();
        UpdateExcludeFromCaptureButtonState();
    }

    private void UpdateMonitorButtonState()
    {
        SwitchMonitorButton.IsEnabled = mainWindow.HasMultipleMonitors() && !mainWindow.IsShowingOnAllMonitors();
        AllMonitorsButton.IsEnabled = mainWindow.HasMultipleMonitors();
    }

    public void UpdateAllMonitorsButtonState()
    {
        UpdateMonitorButtonState();
    }

    public void UpdateExcludeFromCaptureButtonState()
    {
        // Update button opacity to show state (more opaque when active)
        ExcludeFromCaptureButton.Opacity = mainWindow.IsExcludeFromCaptureEnabled() ? 1.0 : 0.5;
    }

    private void BrightnessDown_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.DecreaseBrightness();
    }

    private void BrightnessUp_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.IncreaseBrightness();
    }

    private void ColorCooler_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.DecreaseColorTemperature();
    }

    private void ColorWarmer_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.IncreaseColorTemperature();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.HandleToggle();
    }

    private void SwitchMonitor_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.MoveToNextMonitor();
        UpdateMonitorButtonState();
    }

    private void AllMonitors_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.ToggleAllMonitors();
    }

    private void ExcludeFromCapture_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.ToggleExcludeFromCapture();
        UpdateExcludeFromCaptureButtonState();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
