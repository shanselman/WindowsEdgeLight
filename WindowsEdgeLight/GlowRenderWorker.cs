using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

namespace WindowsEdgeLight;

// The pixel bytes produced by one render, plus the dimensions needed to interpret them.
internal readonly record struct RenderedFrame(byte[] Pixels, int Width, int Height, int Stride);

// Runs GlowBitmapRenderer's WPF off-screen rendering (the expensive part - layout plus
// the blur Effect, measured at several hundred ms depending on monitor DPI/resolution and
// blur radius) on a dedicated background thread with its own WPF Dispatcher, instead of
// the main UI thread.
//
// Why this matters: the main UI thread also owns the global low-level mouse hook
// (SetWindowsHookEx in MainWindow). Windows delivers that hook's notifications
// synchronously on the thread that installed it, and expects the hook procedure to
// return promptly - while that thread is blocked in a long synchronous render, Windows
// can't get a timely response from the hook, which was showing up as system-wide mouse
// lag (not just this app) during color-temperature changes. Moving the render off that
// thread keeps it free to service input while rendering happens in parallel.
//
// WPF Visual/UIElement/RenderTargetBitmap objects have thread affinity - they can only be
// created and used on the thread that instantiated them - which is why this needs its own
// STA thread with its own Dispatcher, not just a plain background Task.
internal sealed class GlowRenderWorker : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher _dispatcher = null!;

    public GlowRenderWorker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "GlowRenderWorker" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }

    // Renders on the worker thread and returns the raw pixel bytes, or null if
    // isStillCurrent() returns false by the time this job reaches the front of the
    // worker's queue - lets a superseded request (e.g. from a fast color-temp drag) bail
    // out before paying for an expensive render whose result would just be discarded.
    public Task<RenderedFrame?> RenderAsync(
        double dipWidth, double dipHeight, double insetWidth, double insetHeight,
        MediaColor color, double blurRadius, double dpiScaleX, double dpiScaleY,
        Func<bool> isStillCurrent)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            if (!isStillCurrent()) return (RenderedFrame?)null;

            var geometry = GlowBitmapRenderer.BuildFrameGeometry(insetWidth, insetHeight, holeCenter: null);
            var bitmap = GlowBitmapRenderer.Render(dipWidth, dipHeight, geometry, color, blurRadius, dpiScaleX, dpiScaleY);

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(System.Windows.Int32Rect.Empty, pixels, stride, 0);

            return (RenderedFrame?)new RenderedFrame(pixels, width, height, stride);
        }).Task;
    }

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _ready.Dispose();
    }
}
