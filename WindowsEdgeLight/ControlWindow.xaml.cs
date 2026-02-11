using System.Windows;
using WindowsEdgeLight.ViewModels;

namespace WindowsEdgeLight;

public partial class ControlWindow : Window
{
    private readonly MainWindow mainWindow;
    private readonly MainViewModel viewModel;

    public ControlWindow(MainWindow main)
    {
        InitializeComponent();
        mainWindow = main;
        viewModel = (MainViewModel)main.DataContext;
        
        // Set DataContext for binding
        DataContext = viewModel;
        
        // Disable switch monitor button if only one monitor
        UpdateMonitorButtonState();
    }

    private void UpdateMonitorButtonState()
    {
        SwitchMonitorButton.IsEnabled = mainWindow.HasMultipleMonitors() && !viewModel.ShowOnAllMonitors;
        AllMonitorsButton.IsEnabled = mainWindow.HasMultipleMonitors();
    }

    public void UpdateAllMonitorsButtonState()
    {
        UpdateMonitorButtonState();
    }

    private void BrightnessDown_Click(object sender, RoutedEventArgs e)
    {
        viewModel.DecreaseBrightnessCommand.Execute(null);
    }

    private void BrightnessUp_Click(object sender, RoutedEventArgs e)
    {
        viewModel.IncreaseBrightnessCommand.Execute(null);
    }

    private void ColorCooler_Click(object sender, RoutedEventArgs e)
    {
        viewModel.DecreaseColorTemperatureCommand.Execute(null);
    }

    private void ColorWarmer_Click(object sender, RoutedEventArgs e)
    {
        viewModel.IncreaseColorTemperatureCommand.Execute(null);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ToggleLightCommand.Execute(null);
    }

    private void SwitchMonitor_Click(object sender, RoutedEventArgs e)
    {
        viewModel.MoveToNextMonitorCommand.Execute(null);
        UpdateMonitorButtonState();
    }

    private void AllMonitors_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ToggleAllMonitorsCommand.Execute(null);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ExitCommand.Execute(null);
    }
}
