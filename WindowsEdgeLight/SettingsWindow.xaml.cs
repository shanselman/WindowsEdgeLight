using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsEdgeLight;

public partial class SettingsWindow : Window
{
    private readonly MainWindow mainWindow;
    private bool isInitializing = true;

    // Coalesces rapid slider-drag ValueChanged events (which can fire dozens of times per
    // second) down to a fixed max rate, always applying the latest value once the interval
    // has passed - same "latest value wins" pattern used for the mouse-hover hook in
    // MainWindow. Each drag still triggers an expensive full-window repaint per applied
    // value, so this bounds how often that happens instead of doing it on every tick.
    private sealed class UpdateThrottle
    {
        private readonly Action<double> _apply;
        private readonly DispatcherTimer _timer;
        private double _pendingValue;
        private bool _hasPending;
        private DateTime _lastAppliedAt = DateTime.MinValue;

        public UpdateThrottle(Action<double> apply, TimeSpan interval)
        {
            _apply = apply;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
            _timer.Tick += (_, _) => Flush();
        }

        public void Request(double value)
        {
            _pendingValue = value;
            _hasPending = true;

            if (DateTime.UtcNow - _lastAppliedAt >= _timer.Interval)
            {
                Flush();
            }
            else if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        public void Cancel()
        {
            _timer.Stop();
            _hasPending = false;
        }

        private void Flush()
        {
            _timer.Stop();
            if (!_hasPending) return;
            _hasPending = false;
            _lastAppliedAt = DateTime.UtcNow;
            _apply(_pendingValue);
        }
    }

    private readonly UpdateThrottle _brightnessThrottle;
    private readonly UpdateThrottle _colorTempThrottle;

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        mainWindow = main;

        _brightnessThrottle = new UpdateThrottle(v => mainWindow.SetBrightness(v, save: false), TimeSpan.FromMilliseconds(33));
        _colorTempThrottle = new UpdateThrottle(v => mainWindow.SetColorTemperature(v, save: false), TimeSpan.FromMilliseconds(33));

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
        UpdateBrightnessLabel();
        _brightnessThrottle.Request(e.NewValue);
    }

    private void BrightnessSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _brightnessThrottle.Cancel();
        mainWindow.SetBrightness(BrightnessSlider.Value, save: true);
    }

    private void ColorTempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isInitializing) return;
        UpdateColorTempLabel();
        _colorTempThrottle.Request(e.NewValue);
    }

    private void ColorTempSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _colorTempThrottle.Cancel();
        mainWindow.SetColorTemperature(ColorTempSlider.Value, save: true);
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner == null)
        {
            return;
        }

        const double gap = 12;
        var ownerHandle = new WindowInteropHelper(Owner).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(ownerHandle);
        var transformFromDevice = PresentationSource.FromVisual(Owner)?
            .CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var workArea = new Rect(
            screen.WorkingArea.X * transformFromDevice.M11,
            screen.WorkingArea.Y * transformFromDevice.M22,
            screen.WorkingArea.Width * transformFromDevice.M11,
            screen.WorkingArea.Height * transformFromDevice.M22);

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
        mainWindow.SaveAppearanceSettings();
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
