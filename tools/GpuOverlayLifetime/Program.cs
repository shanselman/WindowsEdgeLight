using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[2], out var count) || count is < 1 or > 20)
            throw new ArgumentException("GpuOverlayLifetime <generated-production-png> <new-result-json> <1..20 cycles>");
        if (File.Exists(args[1])) throw new IOException("Refusing to overwrite evidence.");
        using var mutex = new Mutex(true, "WindowsEdgeLight_SingleInstance_Mutex", out var owns);
        if (!owns) throw new InvalidOperationException("Another renderer is running.");
        try
        {
            BitmapSource source;
            using (var stream = File.OpenRead(args[0]))
                source = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Pbgra32, null, 0);
            source.Freeze();
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            if (source.PixelWidth != screen.Width || source.PixelHeight != screen.Height)
                throw new InvalidOperationException("Display configuration no longer matches the generated image.");
            var rows = new List<object> { new { cycle = 0, resources = Resources() } };
            for (var cycle = 1; cycle <= count; cycle++)
            {
                var stopwatch = Stopwatch.StartNew();
                using (var gpu = new GpuDevice())
                using (var surface = new GpuMonitor(gpu, screen.X, screen.Y, source))
                {
                    surface.WindowInfo();
                    for (var frame = 0; frame < 8; frame++)
                    {
                        surface.Draw(new Point(300 + frame * 25, 50), 1);
                        Thread.Sleep(33);
                    }
                    var snapshot = surface.Snapshot();
                    var pixel = new byte[4];
                    snapshot.CopyPixels(new System.Windows.Int32Rect((int)(475 * snapshot.DpiX / 96),
                        (int)(50 * snapshot.DpiY / 96), 1, 1), pixel, 4, 0);
                    if (pixel[3] != 0) throw new InvalidOperationException("Lifetime cycle rendered a stale hole.");
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(250);
                rows.Add(new { cycle, elapsedMs = stopwatch.Elapsed.TotalMilliseconds, resources = Resources() });
                Console.WriteLine($"Disposed cycle {cycle}/{count}");
            }
            File.WriteAllText(args[1], JsonSerializer.Serialize(new
            {
                cycles = count, rows,
                scope = "Repeated hardware device/context/swapchain/bitmap/clip/target/HWND creation, rendering, readback and disposal on the primary monitor. Not a production hotplug/device-loss test."
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static object Resources()
    {
        using var p = Process.GetCurrentProcess();
        return new { handles = p.HandleCount, gdi = Native.GetGuiResources(p.Handle, 0),
            user = Native.GetGuiResources(p.Handle, 1), privateBytes = p.PrivateMemorySize64,
            workingSetBytes = p.WorkingSet64 };
    }
}
