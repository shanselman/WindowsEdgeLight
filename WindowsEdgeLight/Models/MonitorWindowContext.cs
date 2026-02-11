using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WindowsEdgeLight.Models;

/// <summary>
/// Context for a monitor window, containing all necessary information for rendering
/// the edge light effect on a specific monitor.
/// </summary>
public class MonitorWindowContext
{
    /// <summary>
    /// Gets or sets the window instance for this monitor.
    /// </summary>
    public Window Window { get; set; } = null!;

    /// <summary>
    /// Gets or sets the screen this window is associated with.
    /// </summary>
    public Screen Screen { get; set; } = null!;

    /// <summary>
    /// Gets or sets the border path element for the edge light effect.
    /// </summary>
    public Path BorderPath { get; set; } = null!;

    /// <summary>
    /// Gets or sets the hover ring ellipse element.
    /// </summary>
    public Ellipse HoverRing { get; set; } = null!;

    /// <summary>
    /// Gets or sets the base geometry for the frame (before hole punch).
    /// </summary>
    public Geometry BaseGeometry { get; set; } = null!;

    /// <summary>
    /// Gets or sets the outer rectangle for frame detection.
    /// </summary>
    public Rect FrameOuterRect { get; set; }

    /// <summary>
    /// Gets or sets the inner rectangle for frame detection.
    /// </summary>
    public Rect FrameInnerRect { get; set; }

    /// <summary>
    /// Gets or sets the X offset of the path geometry within the window.
    /// </summary>
    public double PathOffsetX { get; set; }

    /// <summary>
    /// Gets or sets the Y offset of the path geometry within the window.
    /// </summary>
    public double PathOffsetY { get; set; }

    /// <summary>
    /// Gets or sets the DPI scale factor for the X axis.
    /// </summary>
    public double DpiScaleX { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the DPI scale factor for the Y axis.
    /// </summary>
    public double DpiScaleY { get; set; } = 1.0;
}
