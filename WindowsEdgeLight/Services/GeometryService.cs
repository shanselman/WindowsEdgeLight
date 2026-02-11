using System.Windows;
using System.Windows.Media;
using WindowsEdgeLight.Services.Interfaces;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Service for frame geometry calculations.
/// </summary>
public class GeometryService : IGeometryService
{
    /// <inheritdoc/>
    public (Geometry geometry, double offsetX, double offsetY) CreateFrameGeometry(
        double width, double height, double frameThickness, double outerRadius, double innerRadius)
    {
        // Calculate offsets (centered with margin)
        double offsetX = 20; // 20px margin
        double offsetY = 20;

        // Adjust width and height for margin
        double adjustedWidth = width - 40;  // 20px margin on each side
        double adjustedHeight = height - 40;

        // Create outer rounded rectangle
        var outerRect = new RectangleGeometry(
            new Rect(0, 0, adjustedWidth, adjustedHeight),
            outerRadius,
            outerRadius);

        // Create inner rounded rectangle
        var innerRect = new RectangleGeometry(
            new Rect(
                frameThickness,
                frameThickness,
                adjustedWidth - (frameThickness * 2),
                adjustedHeight - (frameThickness * 2)),
            innerRadius,
            innerRadius);

        // Combine: outer minus inner = frame
        var frameGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);

        return (frameGeometry, offsetX, offsetY);
    }

    /// <inheritdoc/>
    public (Rect outerRect, Rect innerRect) CalculateFrameRectangles(
        double width, double height, double frameThickness, double offsetX, double offsetY, double holeRadius)
    {
        // Adjust width and height for margin
        double adjustedWidth = width - 40;
        double adjustedHeight = height - 40;

        // Expand outer and contract inner rects for earlier hover detection based on ring hole radius
        var outerRect = new Rect(
            offsetX - holeRadius,
            offsetY - holeRadius,
            adjustedWidth + holeRadius * 2,
            adjustedHeight + holeRadius * 2);

        var innerRect = new Rect(
            offsetX + frameThickness + holeRadius,
            offsetY + frameThickness + holeRadius,
            adjustedWidth - (frameThickness * 2) - holeRadius * 2,
            adjustedHeight - (frameThickness * 2) - holeRadius * 2);

        return (outerRect, innerRect);
    }
}
