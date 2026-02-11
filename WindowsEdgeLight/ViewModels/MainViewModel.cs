using System.Windows.Input;
using System.Windows.Media;
using WindowsEdgeLight.Commands;
using Color = System.Windows.Media.Color;

namespace WindowsEdgeLight.ViewModels;

/// <summary>
/// ViewModel for the main window, managing light state and behavior.
/// </summary>
public class MainViewModel : ViewModelBase
{
    #region Constants

    private const double OpacityStep = 0.15;
    private const double MinOpacity = 0.2;
    private const double MaxOpacity = 1.0;
    private const double ColorTempStep = 0.1;
    private const double MinColorTemp = 0.0;
    private const double MaxColorTemp = 1.0;

    #endregion

    #region Fields

    private bool _isLightOn = true;
    private double _currentOpacity = 1.0;
    private double _colorTemperature = 0.5;
    private bool _showOnAllMonitors = false;
    private bool _isControlWindowVisible = true;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the edge light is currently on.
    /// </summary>
    public bool IsLightOn
    {
        get => _isLightOn;
        set
        {
            if (SetProperty(ref _isLightOn, value))
            {
                OnPropertyChanged(nameof(IsLightVisible));
            }
        }
    }

    /// <summary>
    /// Gets whether the light is visible (convenience property for binding).
    /// </summary>
    public bool IsLightVisible => IsLightOn;

    /// <summary>
    /// Gets or sets the current opacity level (brightness) of the edge light.
    /// </summary>
    public double CurrentOpacity
    {
        get => _currentOpacity;
        set => SetProperty(ref _currentOpacity, value);
    }

    /// <summary>
    /// Gets or sets the color temperature (0.0 = cool, 1.0 = warm).
    /// </summary>
    public double ColorTemperature
    {
        get => _colorTemperature;
        set
        {
            if (SetProperty(ref _colorTemperature, Math.Max(MinColorTemp, Math.Min(MaxColorTemp, value))))
            {
                OnPropertyChanged(nameof(ColorTemperatureR));
                OnPropertyChanged(nameof(ColorTemperatureG));
                OnPropertyChanged(nameof(ColorTemperatureB));
                OnPropertyChanged(nameof(EdgeColor));
            }
        }
    }

    /// <summary>
    /// Gets the red component for the current color temperature.
    /// </summary>
    public byte ColorTemperatureR => (byte)(220 + (255 - 220) * _colorTemperature);

    /// <summary>
    /// Gets the green component for the current color temperature.
    /// </summary>
    public byte ColorTemperatureG => (byte)(235 - (235 - 220) * _colorTemperature);

    /// <summary>
    /// Gets the blue component for the current color temperature.
    /// </summary>
    public byte ColorTemperatureB => (byte)(255 - (255 - 180) * _colorTemperature);

    /// <summary>
    /// Gets the current edge light color based on the temperature.
    /// </summary>
    public Color EdgeColor => Color.FromRgb(ColorTemperatureR, ColorTemperatureG, ColorTemperatureB);

    /// <summary>
    /// Gets or sets whether the light is shown on all monitors.
    /// </summary>
    public bool ShowOnAllMonitors
    {
        get => _showOnAllMonitors;
        set => SetProperty(ref _showOnAllMonitors, value);
    }

    /// <summary>
    /// Gets or sets whether the control window is visible.
    /// </summary>
    public bool IsControlWindowVisible
    {
        get => _isControlWindowVisible;
        set => SetProperty(ref _isControlWindowVisible, value);
    }

    #endregion

    #region Commands

    /// <summary>
    /// Command to toggle the edge light on/off.
    /// </summary>
    public ICommand ToggleLightCommand { get; }

    /// <summary>
    /// Command to increase brightness.
    /// </summary>
    public ICommand IncreaseBrightnessCommand { get; }

    /// <summary>
    /// Command to decrease brightness.
    /// </summary>
    public ICommand DecreaseBrightnessCommand { get; }

    /// <summary>
    /// Command to increase color temperature (warmer).
    /// </summary>
    public ICommand IncreaseColorTemperatureCommand { get; }

    /// <summary>
    /// Command to decrease color temperature (cooler).
    /// </summary>
    public ICommand DecreaseColorTemperatureCommand { get; }

    /// <summary>
    /// Command to move to the next monitor.
    /// </summary>
    public ICommand MoveToNextMonitorCommand { get; }

    /// <summary>
    /// Command to toggle showing on all monitors.
    /// </summary>
    public ICommand ToggleAllMonitorsCommand { get; }

    /// <summary>
    /// Command to toggle control window visibility.
    /// </summary>
    public ICommand ToggleControlsVisibilityCommand { get; }

    /// <summary>
    /// Command to show help dialog.
    /// </summary>
    public ICommand ShowHelpCommand { get; }

    /// <summary>
    /// Command to exit the application.
    /// </summary>
    public ICommand ExitCommand { get; }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the MainViewModel class.
    /// </summary>
    public MainViewModel()
    {
        // Initialize commands
        ToggleLightCommand = new RelayCommand(_ => ToggleLight());
        IncreaseBrightnessCommand = new RelayCommand(
            _ => IncreaseBrightness(),
            _ => CurrentOpacity < MaxOpacity);
        DecreaseBrightnessCommand = new RelayCommand(
            _ => DecreaseBrightness(),
            _ => CurrentOpacity > MinOpacity);
        IncreaseColorTemperatureCommand = new RelayCommand(
            _ => IncreaseColorTemperature(),
            _ => ColorTemperature < MaxColorTemp);
        DecreaseColorTemperatureCommand = new RelayCommand(
            _ => DecreaseColorTemperature(),
            _ => ColorTemperature > MinColorTemp);
        MoveToNextMonitorCommand = new RelayCommand(_ => MoveToNextMonitor());
        ToggleAllMonitorsCommand = new RelayCommand(_ => ToggleAllMonitors());
        ToggleControlsVisibilityCommand = new RelayCommand(_ => ToggleControlsVisibility());
        ShowHelpCommand = new RelayCommand(_ => ShowHelp());
        ExitCommand = new RelayCommand(_ => Exit());
    }

    #endregion

    #region Methods

    /// <summary>
    /// Toggles the edge light on or off.
    /// </summary>
    public void ToggleLight()
    {
        IsLightOn = !IsLightOn;
    }

    /// <summary>
    /// Increases the brightness of the edge light.
    /// </summary>
    public void IncreaseBrightness()
    {
        CurrentOpacity = Math.Min(MaxOpacity, CurrentOpacity + OpacityStep);
    }

    /// <summary>
    /// Decreases the brightness of the edge light.
    /// </summary>
    public void DecreaseBrightness()
    {
        CurrentOpacity = Math.Max(MinOpacity, CurrentOpacity - OpacityStep);
    }

    /// <summary>
    /// Increases the color temperature (makes it warmer).
    /// </summary>
    public void IncreaseColorTemperature()
    {
        ColorTemperature += ColorTempStep;
    }

    /// <summary>
    /// Decreases the color temperature (makes it cooler).
    /// </summary>
    public void DecreaseColorTemperature()
    {
        ColorTemperature -= ColorTempStep;
    }

    /// <summary>
    /// Moves the edge light to the next monitor.
    /// </summary>
    private void MoveToNextMonitor()
    {
        // This will be handled by the view/service layer
        // Raise an event for the view to handle
        OnMoveToNextMonitor?.Invoke();
    }

    /// <summary>
    /// Toggles showing the edge light on all monitors.
    /// </summary>
    private void ToggleAllMonitors()
    {
        // Raise an event for the view to handle the actual toggle
        OnToggleAllMonitors?.Invoke();
    }

    /// <summary>
    /// Toggles the visibility of the control window.
    /// </summary>
    private void ToggleControlsVisibility()
    {
        IsControlWindowVisible = !IsControlWindowVisible;
    }

    /// <summary>
    /// Shows the help dialog.
    /// </summary>
    private void ShowHelp()
    {
        OnShowHelp?.Invoke();
    }

    /// <summary>
    /// Exits the application.
    /// </summary>
    private void Exit()
    {
        OnExit?.Invoke();
    }

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the user requests to move to the next monitor.
    /// </summary>
    public event Action? OnMoveToNextMonitor;

    /// <summary>
    /// Event raised when the user toggles all monitors mode.
    /// </summary>
    public event Action? OnToggleAllMonitors;

    /// <summary>
    /// Event raised when the user requests to show help.
    /// </summary>
    public event Action? OnShowHelp;

    /// <summary>
    /// Event raised when the user requests to exit the application.
    /// </summary>
    public event Action? OnExit;

    #endregion
}
