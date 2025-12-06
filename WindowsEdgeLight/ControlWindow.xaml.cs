using System.Windows;
// using WindowsEdgeLight.AI;  // Temporarily disabled for debugging

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
        
        // Show AI button only if hardware supports it
        // UpdateAIButtonVisibility();  // Temporarily disabled
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

    private void UpdateAIButtonVisibility()
    {
        // Always show for now - debugging
        AITrackingButton.Visibility = Visibility.Visible;
    }

    public void UpdateAITrackingButtonState(bool isEnabled, bool isLoading = false)
    {
        // Update button appearance based on tracking state
        if (isLoading)
        {
            AITrackingButton.Content = "⏳";
            AITrackingButton.ToolTip = "AI Face Tracking: Starting...";
            AITrackingButton.IsEnabled = false;
        }
        else
        {
            AITrackingButton.Content = isEnabled ? "👁" : "👤";
            AITrackingButton.ToolTip = isEnabled 
                ? "AI Face Tracking: ON (click to disable)" 
                : "AI Face Tracking: OFF (click to enable)";
            AITrackingButton.IsEnabled = true;
        }
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

    private async void AITracking_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            mainWindow.ToggleAIFaceTracking();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error: {ex.Message}\n\n{ex.StackTrace}", "AI Tracking Error");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
