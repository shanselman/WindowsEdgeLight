using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Binding = System.Windows.Data.Binding;

namespace WindowsEdgeLight;

// Experimental presenter: shares the unchanged production glow between four
// non-overlapping layered surfaces. The main window retains controls/hotkeys.
internal sealed class EdgeStripPresenter : IDisposable
{
    private readonly Window owner;
    private readonly Grid glowHost;
    private readonly FrameworkElement source;
    private readonly Grid brushSource;
    private readonly List<(Window Window, FrameworkElement Host, Rect Viewport)> strips = [];
    public IEnumerable<Window> Windows => strips.Select(s => s.Window);

    public EdgeStripPresenter(Window owner, Grid glowHost)
    {
        this.owner = owner;
        this.glowHost = glowHost;
        source = (FrameworkElement)glowHost.Children[0];
        glowHost.Children.Remove(source);
        glowHost.Visibility = Visibility.Collapsed;
        brushSource = new Grid
        {
            Width = owner.ActualWidth, Height = owner.ActualHeight,
            Background = Brushes.Transparent, ClipToBounds = true
        };
        brushSource.Children.Add(source);
        brushSource.Measure(new Size(owner.ActualWidth, owner.ActualHeight));
        brushSource.Arrange(new Rect(0, 0, owner.ActualWidth, owner.ActualHeight));
        var dpi = VisualTreeHelper.GetDpi(owner);
        var fullWidth = (int)Math.Round(owner.ActualWidth * dpi.DpiScaleX);
        var fullHeight = (int)Math.Round(owner.ActualHeight * dpi.DpiScaleY);
        var bandX = Math.Min((int)Math.Ceiling(176 * dpi.DpiScaleX), fullWidth / 2);
        var bandY = Math.Min((int)Math.Ceiling(176 * dpi.DpiScaleY), fullHeight / 2);
        var pixels = new[]
        {
            new Rect(0, 0, fullWidth, bandY),
            new Rect(0, fullHeight - bandY, fullWidth, bandY),
            new Rect(0, bandY, bandX, fullHeight - 2 * bandY),
            new Rect(fullWidth - bandX, bandY, bandX, fullHeight - 2 * bandY)
        };
        GetWindowRect(new WindowInteropHelper(owner).Handle, out var ownerRect);
        GetWindowDisplayAffinity(new WindowInteropHelper(owner).Handle, out var affinity);
        foreach (var pixel in pixels)
        {
            var viewport = new Rect(pixel.X / dpi.DpiScaleX, pixel.Y / dpi.DpiScaleY,
                pixel.Width / dpi.DpiScaleX, pixel.Height / dpi.DpiScaleY);
            var host = new StripVisual(new VisualBrush(brushSource)
            {
                AutoLayoutContent = false,
                ViewboxUnits = BrushMappingMode.Absolute,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, owner.ActualWidth, owner.ActualHeight),
                Viewport = new Rect(-viewport.X, -viewport.Y, owner.ActualWidth, owner.ActualHeight),
                Stretch = Stretch.Fill
            }, viewport);
            host.SetBinding(UIElement.OpacityProperty, new Binding(nameof(UIElement.Opacity)) { Source = glowHost });
            var window = new Window
            {
                Title = "Windows Edge Light (strip experiment)", WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false,
                Topmost = true, Owner = owner, Content = host,
                Left = (ownerRect.Left + pixel.X) / dpi.DpiScaleX,
                Top = (ownerRect.Top + pixel.Y) / dpi.DpiScaleY,
                Width = viewport.Width, Height = viewport.Height
            };
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x80000 | 0x08000000);
                SetWindowDisplayAffinity(hwnd, affinity);
            };
            window.Show();
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
                ownerRect.Left + (int)pixel.X, ownerRect.Top + (int)pixel.Y,
                (int)pixel.Width, (int)pixel.Height, 0x14);
            strips.Add((window, host, viewport));
        }
    }

    public void UpdateHole()
    {
        var hole = (glowHost.Clip as CombinedGeometry)?.Geometry2 as EllipseGeometry;
        foreach (var strip in strips)
        {
            if (hole == null || !strip.Viewport.IntersectsWith(hole.Bounds))
            {
                if (strip.Host.Clip != null) strip.Host.Clip = null;
                continue;
            }
            var point = new Point(hole.Center.X - strip.Viewport.X, hole.Center.Y - strip.Viewport.Y);
            if (strip.Host.Clip is CombinedGeometry clip && clip.Geometry2 is EllipseGeometry ellipse)
            {
                ellipse.Center = point;
            }
            else
            {
                strip.Host.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, strip.Viewport.Width, strip.Viewport.Height)),
                    new EllipseGeometry(point, hole.RadiusX, hole.RadiusY));
            }
        }
    }

    public void Dispose()
    {
        foreach (var strip in strips) strip.Window.Close();
        strips.Clear();
        brushSource.Children.Remove(source);
        glowHost.Children.Add(source);
        glowHost.Visibility = Visibility.Visible;
    }

    private sealed class StripVisual(VisualBrush brush, Rect viewport) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, viewport.Width, viewport.Height)));
            drawingContext.DrawRectangle(brush, null, new Rect(0, 0, viewport.Width, viewport.Height));
            drawingContext.Pop();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
