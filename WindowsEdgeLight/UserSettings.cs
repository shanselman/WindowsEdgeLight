namespace WindowsEdgeLight;

public class UserSettings
{
    public string Version { get; set; } = "1.0";
    public bool IsLightOn { get; set; } = true;
    public double Brightness { get; set; } = 1.0;
    public double ColorTemperature { get; set; } = 0.5;
    public int CurrentMonitorIndex { get; set; } = 0;
    public bool ShowOnAllMonitors { get; set; } = false;
    public bool IsControlWindowVisible { get; set; } = true;

    public static UserSettings Default => new()
    {
        Version = "1.0",
        IsLightOn = true,
        Brightness = 1.0,
        ColorTemperature = 0.5,
        CurrentMonitorIndex = 0,
        ShowOnAllMonitors = false,
        IsControlWindowVisible = true
    };

    public void Validate()
    {
        Brightness = Math.Clamp(Brightness, 0.2, 1.0);
        ColorTemperature = Math.Clamp(ColorTemperature, 0.0, 1.0);
        CurrentMonitorIndex = Math.Max(0, CurrentMonitorIndex);
    }
}
