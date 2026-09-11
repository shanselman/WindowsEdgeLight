using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace WindowsEdgeLight;

// Builds the glow frame geometry and renders it off-screen to a bitmap. Shared by
// MainWindow and every additional-monitor NativeLayeredWindow, so the visual math (frame
// thickness, corner radii, gradient stops, blur) lives in exactly one place instead of
// being duplicated per window as it was before.
internal static class GlowBitmapRenderer
{
    public const double FrameThickness = 80;
    public const double OuterRadius = 100;
    public const double InnerRadius = 60;
    // Matches the old HoverCursorRing's 140px diameter - the hole cut into the frame at
    // the cursor position is the same size the ring used to be.
    public const double HoleRadius = 70;

    // insetWidth/insetHeight are the frame's own bounds (already margin-adjusted, i.e.
    // full window size minus 40) - holeCenter, if given, is in that same local coordinate
    // space (see MainWindow's pathOffsetX/Y math for converting cursor position into it).
    public static Geometry BuildFrameGeometry(double insetWidth, double insetHeight, System.Windows.Point? holeCenter)
    {
        var outerRect = new RectangleGeometry(new Rect(0, 0, insetWidth, insetHeight), OuterRadius, OuterRadius);
        var innerRect = new RectangleGeometry(
            new Rect(FrameThickness, FrameThickness, insetWidth - FrameThickness * 2, insetHeight - FrameThickness * 2),
            InnerRadius, InnerRadius);
        Geometry frame = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);

        if (holeCenter is System.Windows.Point center)
        {
            var hole = new EllipseGeometry(center, HoleRadius, HoleRadius);
            frame = new CombinedGeometry(GeometryCombineMode.Exclude, frame, hole);
        }

        return frame;
    }

    // fullDipWidth/fullDipHeight are the full window size in DIPs (matching what the
    // window used to be); frameGeometry must have been built with insetWidth/Height of
    // (fullDipWidth - 40, fullDipHeight - 40) so the WPF layout centers it identically to
    // the original XAML's HorizontalAlignment/VerticalAlignment="Center" behavior.
    public static RenderTargetBitmap Render(
        double fullDipWidth, double fullDipHeight, Geometry frameGeometry,
        MediaColor temperatureColor, double blurRadius, double dpiScaleX, double dpiScaleY)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        gradient.GradientStops.Add(new GradientStop(MediaColor.FromRgb(255, 255, 255), 0.0));
        gradient.GradientStops.Add(new GradientStop(temperatureColor, 0.3));
        gradient.GradientStops.Add(new GradientStop(temperatureColor, 0.5));
        gradient.GradientStops.Add(new GradientStop(temperatureColor, 0.7));
        gradient.GradientStops.Add(new GradientStop(MediaColor.FromRgb(255, 255, 255), 1.0));

        var path = new System.Windows.Shapes.Path
        {
            Data = frameGeometry,
            Fill = gradient,
            Stretch = Stretch.None,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = blurRadius,
                Opacity = 1,
                ShadowDepth = 0,
                Color = temperatureColor
            }
        };

        var grid = new Grid { Width = fullDipWidth, Height = fullDipHeight };
        grid.Children.Add(path);

        // Off-screen elements need an explicit Measure/Arrange pass - they're never
        // attached to a live visual tree, so nothing else will lay them out.
        grid.Measure(new System.Windows.Size(fullDipWidth, fullDipHeight));
        grid.Arrange(new Rect(0, 0, fullDipWidth, fullDipHeight));

        int pixelWidth = Math.Max(1, (int)Math.Round(fullDipWidth * dpiScaleX));
        int pixelHeight = Math.Max(1, (int)Math.Round(fullDipHeight * dpiScaleY));

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0 * dpiScaleX, 96.0 * dpiScaleY, PixelFormats.Pbgra32);
        rtb.Render(grid);
        return rtb;
    }
}
