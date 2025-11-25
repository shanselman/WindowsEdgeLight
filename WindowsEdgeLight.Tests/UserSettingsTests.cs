using FluentAssertions;
using WindowsEdgeLight;

namespace WindowsEdgeLight.Tests;

public class UserSettingsTests
{
    [Fact]
    public void Default_ReturnsSettingsWithExpectedDefaults()
    {
        var settings = UserSettings.Default;

        settings.Version.Should().Be("1.0");
        settings.IsLightOn.Should().BeTrue();
        settings.Brightness.Should().Be(1.0);
        settings.ColorTemperature.Should().Be(0.5);
        settings.CurrentMonitorIndex.Should().Be(0);
        settings.ShowOnAllMonitors.Should().BeFalse();
        settings.IsControlWindowVisible.Should().BeTrue();
    }

    [Fact]
    public void Validate_ClampsBrightness_WhenTooLow()
    {
        var settings = new UserSettings { Brightness = 0.1 };
        
        settings.Validate();
        
        settings.Brightness.Should().Be(0.2);
    }

    [Fact]
    public void Validate_ClampsBrightness_WhenTooHigh()
    {
        var settings = new UserSettings { Brightness = 1.5 };
        
        settings.Validate();
        
        settings.Brightness.Should().Be(1.0);
    }

    [Fact]
    public void Validate_KeepsBrightness_WhenInValidRange()
    {
        var settings = new UserSettings { Brightness = 0.75 };
        
        settings.Validate();
        
        settings.Brightness.Should().Be(0.75);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_KeepsBrightness_AtBoundaries(double brightness)
    {
        var settings = new UserSettings { Brightness = brightness };
        
        settings.Validate();
        
        settings.Brightness.Should().Be(brightness);
    }

    [Fact]
    public void Validate_ClampsColorTemperature_WhenTooLow()
    {
        var settings = new UserSettings { ColorTemperature = -0.5 };
        
        settings.Validate();
        
        settings.ColorTemperature.Should().Be(0.0);
    }

    [Fact]
    public void Validate_ClampsColorTemperature_WhenTooHigh()
    {
        var settings = new UserSettings { ColorTemperature = 1.5 };
        
        settings.Validate();
        
        settings.ColorTemperature.Should().Be(1.0);
    }

    [Theory]
    [InlineData(-0.1, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.3, 0.3)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.8, 0.8)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    public void Validate_ClampsColorTemperature_Correctly(double input, double expected)
    {
        var settings = new UserSettings { ColorTemperature = input };
        
        settings.Validate();
        
        settings.ColorTemperature.Should().Be(expected);
    }

    [Fact]
    public void Validate_ClampsNegativeMonitorIndex_ToZero()
    {
        var settings = new UserSettings { CurrentMonitorIndex = -5 };
        
        settings.Validate();
        
        settings.CurrentMonitorIndex.Should().Be(0);
    }

    [Fact]
    public void Validate_KeepsPositiveMonitorIndex_Unchanged()
    {
        var settings = new UserSettings { CurrentMonitorIndex = 3 };
        
        settings.Validate();
        
        settings.CurrentMonitorIndex.Should().Be(3);
    }

    [Fact]
    public void Validate_DoesNotModify_BooleanProperties()
    {
        var settings = new UserSettings
        {
            IsLightOn = false,
            ShowOnAllMonitors = true,
            IsControlWindowVisible = false
        };
        
        settings.Validate();
        
        settings.IsLightOn.Should().BeFalse();
        settings.ShowOnAllMonitors.Should().BeTrue();
        settings.IsControlWindowVisible.Should().BeFalse();
    }

    [Fact]
    public void Validate_HandlesMultipleInvalidValues_Simultaneously()
    {
        var settings = new UserSettings
        {
            Brightness = 5.0,
            ColorTemperature = -2.0,
            CurrentMonitorIndex = -1
        };
        
        settings.Validate();
        
        settings.Brightness.Should().Be(1.0);
        settings.ColorTemperature.Should().Be(0.0);
        settings.CurrentMonitorIndex.Should().Be(0);
    }
}
