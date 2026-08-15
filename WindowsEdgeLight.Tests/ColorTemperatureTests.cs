using System.Windows.Media;
using Xunit;
using MediaColor = System.Windows.Media.Color;

namespace WindowsEdgeLight.Tests;

/// <summary>
/// Tests for the <see cref="MainWindow.GetColorForTemperature"/> linear-interpolation helper.
/// </summary>
public class ColorTemperatureTests
{
    [Fact]
    public void CoolestTemperatureReturnsCoolColor()
    {
        var result = MainWindow.GetColorForTemperature(0.0);

        Assert.Equal(MainWindow.CoolColor, result);
    }

    [Fact]
    public void WarmestTemperatureReturnsWarmColor()
    {
        var result = MainWindow.GetColorForTemperature(1.0);

        Assert.Equal(MainWindow.WarmColor, result);
    }

    [Fact]
    public void MidpointReturnsMidpointColor()
    {
        var result = MainWindow.GetColorForTemperature(0.5);

        byte ExpectedChannel(byte cool, byte warm) => (byte)(cool + ((warm - cool) * 0.5));

        Assert.Equal(ExpectedChannel(MainWindow.CoolColor.R, MainWindow.WarmColor.R), result.R);
        Assert.Equal(ExpectedChannel(MainWindow.CoolColor.G, MainWindow.WarmColor.G), result.G);
        Assert.Equal(ExpectedChannel(MainWindow.CoolColor.B, MainWindow.WarmColor.B), result.B);
    }

    [Fact]
    public void QuarterTemperatureInterpolatesCorrectly()
    {
        var result = MainWindow.GetColorForTemperature(0.25);

        byte ExpectedChannel(byte cool, byte warm) => (byte)(cool + ((warm - cool) * 0.25));

        Assert.Equal(ExpectedChannel(MainWindow.CoolColor.R, MainWindow.WarmColor.R), result.R);
        Assert.Equal(ExpectedChannel(MainWindow.CoolColor.G, MainWindow.WarmColor.G), result.G);
        Assert.Equal(ExpectedChannel(MainWindow.CoolColor.B, MainWindow.WarmColor.B), result.B);
    }

    [Fact]
    public void AlphaChannelIsAlwaysFullyOpaque()
    {
        Assert.Equal(255, MainWindow.GetColorForTemperature(0.0).A);
        Assert.Equal(255, MainWindow.GetColorForTemperature(0.5).A);
        Assert.Equal(255, MainWindow.GetColorForTemperature(1.0).A);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void AllTemperaturesBrightnessChannelStaysInByteRange(double temperature)
    {
        var result = MainWindow.GetColorForTemperature(temperature);

        // No channel should overflow; all values must be valid bytes (0–255)
        Assert.InRange((int)result.R, 0, 255);
        Assert.InRange((int)result.G, 0, 255);
        Assert.InRange((int)result.B, 0, 255);
    }
}
