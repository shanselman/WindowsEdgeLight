using System.Windows;

namespace WindowsEdgeLight;

public partial class ControlWindow : Window
{
    private readonly MainWindow mainWindow;

    public ControlWindow(MainWindow main)
    {
        InitializeComponent();
        mainWindow = main;
        
        UpdateMonitorButtonState();
        ApplyButtonVisibility();
    }

    private void UpdateMonitorButtonState()
    {
        SwitchMonitorButton.IsEnabled = mainWindow.HasMultipleMonitors() && !mainWindow.IsShowingOnAllMonitors();
        AllMonitorsButton.IsEnabled = mainWindow.HasMultipleMonitors();
    }

    public void ApplyButtonVisibility()
    {
        var toggleButtonVis = mainWindow.GetIsToggleButtonVisible() ? Visibility.Visible : Visibility.Collapsed;
        var brightVis = mainWindow.GetIsBrightnessButtonsVisible() ? Visibility.Visible : Visibility.Collapsed;
        var tempVis = mainWindow.GetIsColorTempButtonsVisible() ? Visibility.Visible : Visibility.Collapsed;
        var monitorCtrlVis = mainWindow.GetIsControlMonitorsButtonVisible() ? Visibility.Visible : Visibility.Collapsed;

        ToggleLightButton.Visibility = toggleButtonVis;
        BrightnessDownButton.Visibility = brightVis;
        BrightnessUpButton.Visibility = brightVis;
        ColorWarmerButton.Visibility = tempVis;
        ColorCoolerButton.Visibility = tempVis;
        SwitchMonitorButton.Visibility = monitorCtrlVis;
        AllMonitorsButton.Visibility = monitorCtrlVis;
    }

    public void UpdateAllMonitorsButtonState()
    {
        UpdateMonitorButtonState();
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

    private void Open_Settings(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(mainWindow);
        settings.Owner = this;
        settings.ShowDialog();
        ApplyButtonVisibility();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
