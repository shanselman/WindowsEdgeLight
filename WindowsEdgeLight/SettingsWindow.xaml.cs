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

        BrightnessSlider.Value = mainWindow.GetBrightness();
        ColorTempSlider.Value = mainWindow.GetColorTemperature();
        ExcludeFromCaptureCheckBox.IsChecked = mainWindow.IsExcludeFromCaptureEnabled();
        ShowBrightnessCheckBox.IsChecked = mainWindow.GetIsBrightnessButtonsVisible();
        ShowColorTempCheckBox.IsChecked = mainWindow.GetIsColorTempButtonsVisible();
        ShowMonitorControlsCheckBox.IsChecked = mainWindow.GetIsControlMonitorsButtonVisible();
        ShowToggleCheckBox.IsChecked = mainWindow.GetIsToggleButtonVisible();

        UpdateBrightnessLabel();
        UpdateColorTempLabel();

        isInitializing = false;
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isInitializing) return;
        mainWindow.SetBrightness(e.NewValue, save: false);
        UpdateBrightnessLabel();
    }

    private void BrightnessSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        mainWindow.SetBrightness(BrightnessSlider.Value, save: true);
    }

    private void ColorTempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isInitializing) return;
        mainWindow.SetColorTemperature(e.NewValue, save: false);
        UpdateColorTempLabel();
    }

    private void ColorTempSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        mainWindow.SetColorTemperature(ColorTempSlider.Value, save: true);
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner == null)
        {
            return;
        }

        const double gap = 12;
        var workArea = SystemParameters.WorkArea;
        MaxHeight = Math.Max(200, workArea.Height - (gap * 2));
        var desiredLeft = Owner.Left + ((Owner.ActualWidth - ActualWidth) / 2);
        Left = Math.Min(Math.Max(desiredLeft, workArea.Left + gap), workArea.Right - ActualWidth - gap);

        var aboveOwnerTop = Owner.Top - ActualHeight - gap;
        if (aboveOwnerTop >= workArea.Top + gap)
        {
            Top = aboveOwnerTop;
            return;
        }

        var belowOwnerTop = Owner.Top + Owner.ActualHeight + gap;
        if (belowOwnerTop + ActualHeight <= workArea.Bottom - gap)
        {
            Top = belowOwnerTop;
            return;
        }

        Top = workArea.Top + ((workArea.Height - ActualHeight) / 2);
    }

    private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        mainWindow.SetBrightness(BrightnessSlider.Value, save: true);
        mainWindow.SetColorTemperature(ColorTempSlider.Value, save: true);
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

    private void ShowToggle_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.SetIsToggleVisible(ShowToggleCheckBox.IsChecked == true);
        UpdateOwnerControlWindow();
    }

    private void ShowBrightness_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.SetIsBrightnessButtonsVisible(ShowBrightnessCheckBox.IsChecked == true);
        UpdateOwnerControlWindow();
    }

    private void ShowColorTemp_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.SetIsColorTempButtonsVisible(ShowColorTempCheckBox.IsChecked == true);
        UpdateOwnerControlWindow();
    }

    private void ShowMonitorControls_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.SetIsControlMonitorsButtonVisible(ShowMonitorControlsCheckBox.IsChecked == true);
        UpdateOwnerControlWindow();
    }

    private void ResetControlBarPosition_Click(object sender, RoutedEventArgs e)
    {
        mainWindow.ResetControlWindowPosition();
    }

    private void UpdateOwnerControlWindow()
    {
        if (Owner is ControlWindow controlWindow)
        {
            controlWindow.ApplyButtonVisibility();
        }
    }
}
