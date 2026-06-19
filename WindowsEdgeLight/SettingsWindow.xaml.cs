using System.Windows;
using System.Windows.Controls;

namespace WindowsEdgeLight;

public partial class SettingsWindow : Window
{
    private readonly MainWindow mainWindow;
    private bool isInitializing = true;

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        mainWindow = main;

        var settings = mainWindow.GetSettings();

        BrightnessSlider.Value = mainWindow.GetBrightness();
        ColorTempSlider.Value = mainWindow.GetColorTemperature();
        ExcludeFromCaptureCheckBox.IsChecked = mainWindow.IsExcludeFromCaptureEnabled();
        ShowBrightnessCheckBox.IsChecked = settings.ShowBrightnessButtons;
        ShowColorTempCheckBox.IsChecked = settings.ShowColorTempButtons;
        ShowMonitorControlsCheckBox.IsChecked = settings.ShowWindowControlButtons;

        UpdateBrightnessLabel();
        UpdateColorTempLabel();

        isInitializing = false;
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isInitializing) return;
        mainWindow.SetBrightness(e.NewValue);
        UpdateBrightnessLabel();
    }

    private void ColorTempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isInitializing) return;
        mainWindow.SetColorTemperature(e.NewValue);
        UpdateColorTempLabel();
    }

    private void ExcludeFromCapture_Click(object sender, RoutedEventArgs e)
    {
        bool current = mainWindow.IsExcludeFromCaptureEnabled();
        bool desired = ExcludeFromCaptureCheckBox.IsChecked == true;
        if (current != desired)
        {
            mainWindow.ToggleExcludeFromCapture();
        }
    }

    private void UpdateBrightnessLabel()
    {
        if (BrightnessValueText != null)
            BrightnessValueText.Text = $"{(int)(BrightnessSlider.Value * 100)}%";
    }

    private void UpdateColorTempLabel()
    {
        if (ColorTempValueText != null)
            ColorTempValueText.Text = $"{(int)(ColorTempSlider.Value * 100)}%";
    }

    private void ShowBrightness_Click(object sender, RoutedEventArgs e)
    {
        var settings = mainWindow.GetSettings();
        settings.ShowBrightnessButtons = ShowBrightnessCheckBox.IsChecked == true;
        settings.Save();
        UpdateOwnerControlWindow();
    }

    private void ShowColorTemp_Click(object sender, RoutedEventArgs e)
    {
        var settings = mainWindow.GetSettings();
        settings.ShowColorTempButtons = ShowColorTempCheckBox.IsChecked == true;
        settings.Save();
        UpdateOwnerControlWindow();
    }

    private void ShowMonitorControls_Click(object sender, RoutedEventArgs e)
    {
        var settings = mainWindow.GetSettings();
        settings.ShowWindowControlButtons = ShowMonitorControlsCheckBox.IsChecked == true;
        settings.Save();
        UpdateOwnerControlWindow();
    }

    private void UpdateOwnerControlWindow()
    {
        if (Owner is ControlWindow controlWindow)
        {
            controlWindow.ApplyButtonVisibility();
        }
    }
}
