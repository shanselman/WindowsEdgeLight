using System.Windows;

namespace WindowsEdgeLight.Services.Interfaces;

/// <summary>
/// Service for managing frame geometry calculations.
/// </summary>
public interface IGeometryService
{
    /// <summary>
    /// Creates the frame geometry for the edge light effect.
    /// </summary>
    /// <param name="width">Width of the window.</param>
    /// <param name="height">Height of the window.</param>
    /// <param name="frameThickness">Thickness of the frame border.</param>
    /// <param name="outerRadius">Radius for outer corners.</param>
    /// <param name="innerRadius">Radius for inner corners.</param>
    /// <returns>Tuple containing the frame geometry and offset coordinates.</returns>
    (System.Windows.Media.Geometry geometry, double offsetX, double offsetY) CreateFrameGeometry(
        double width, double height, double frameThickness, double outerRadius, double innerRadius);

    /// <summary>
    /// Calculates the frame rectangles for hit testing.
    /// </summary>
    /// <param name="width">Width of the window.</param>
    /// <param name="height">Height of the window.</param>
    /// <param name="frameThickness">Thickness of the frame border.</param>
    /// <param name="offsetX">X offset of the geometry.</param>
    /// <param name="offsetY">Y offset of the geometry.</param>
    /// <param name="holeRadius">Radius of the hole punch effect.</param>
    /// <returns>Tuple containing outer and inner rectangles for hit detection.</returns>
    (Rect outerRect, Rect innerRect) CalculateFrameRectangles(
        double width, double height, double frameThickness, double offsetX, double offsetY, double holeRadius);
}
