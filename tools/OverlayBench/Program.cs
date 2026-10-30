using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowsEdgeLight;
using Point = System.Windows.Point;
using Application = System.Windows.Application;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MainWindow main = null!;
    private static readonly List<object> samples = [];
    private static readonly List<double> intervals = [];
    private static readonly List<Window> windows = [];
    private static readonly List<object> presenters = [];
    private static readonly Process process = Process.GetCurrentProcess();
    private static Process? dwm;
    private static string output = "";
    private static string lane = "hover";
    private static int duration;
    private static DispatcherTimer? traceTimer;
    private static DispatcherTimer? sampleTimer;
    private static Stopwatch clock = new();
    private static double lastTick;
    private static long lastCpu;
    private static long lastDwm;
    private static double lastSample;
    private static int ticks;
    private static int updates;
    private static double maxUpdateMs;
    private static string? dwmError;
    private static readonly List<(System.Windows.Forms.Screen Screen, double DpiX, double DpiY)> monitors = [];
    private static object? startResources;
    private static object? metadata;
    private static DateTime measuredStartedUtc;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[2], out duration))
        {
            Console.Error.WriteLine("OverlayBench <new-output-directory> <hover|monitors|brightness|color|idle|off> <seconds>");
            return 2;
        }
        output = Path.GetFullPath(args[0]);
        lane = args[1];
        if (Directory.Exists(output)) throw new IOException("Refusing to overwrite evidence: " + output);
        Directory.CreateDirectory(output);
        using var mutex = new Mutex(true, "WindowsEdgeLight_SingleInstance_Mutex", out var owns);
        if (!owns) throw new InvalidOperationException("A real app or another benchmark is running.");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), e.Exception.ToString());
            e.Handled = true;
            Cleanup();
            application.Shutdown(1);
        };
        application.Startup += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString());
                Cleanup();
                application.Shutdown(1);
            }
        };
        try { return application.Run(); }
        finally { Cleanup(); mutex.ReleaseMutex(); }
    }

    private static object? Field(string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(main);
    private static void Set(string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(main, value);
    private static object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(main, args);

    private static async Task Run()
    {
        main = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
        Set("isLightOn", true);
        Set("isControlWindowVisible", false);
        ((System.Windows.Forms.NotifyIcon)Field("notifyIcon")!).Visible = false;
        main.Show();
        ((DispatcherTimer)Field("cursorPollTimer")!).Stop();
        main.SetBrightness(1, save: false);
        main.SetColorTemperature(.5, save: false);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var dpi = ((double, double))Call("GetDpiForScreen", screen)!;
            monitors.Add((screen, dpi.Item1, dpi.Item2));
            if (screen.Primary) continue;
            var ctx = Call("CreateMonitorWindow", screen)!;
            var window = (Window)ctx.GetType().GetProperty("Window")!.GetValue(ctx)!;
            window.ShowActivated = false;
            ((System.Collections.IList)Field("additionalMonitorWindows")!).Add(ctx);
            window.Show();
        }
        Call("UpdateAdditionalMonitorWindows");
        foreach (Window window in Application.Current.Windows)
            if (window.IsVisible) windows.Add(window);
        if (windows.Count < System.Windows.Forms.Screen.AllScreens.Length)
            throw new InvalidOperationException("Missing visible monitor windows.");
        var settings = (AppSettings)Field("settings")!;
        // This only changes the in-memory instance. No settings Save calls are made.
        settings.ExcludeFromCapture = true;
        Call("ApplyExcludeFromCapture");
        var presenterType = typeof(MainWindow).Assembly.GetType("WindowsEdgeLight.EdgeStripPresenter");
        if (presenterType != null)
        {
            foreach (var window in windows)
            {
                var host = window == main ? (System.Windows.Controls.Grid)Field("GlowHost")! :
                    (System.Windows.Controls.Grid)window.Content;
                presenters.Add(Activator.CreateInstance(presenterType, window, host)!);
            }
        }
        dwm = Process.GetProcessesByName("dwm").FirstOrDefault(p => p.SessionId == process.SessionId);
        await Task.Delay(5000);
        Call("ClearHoverHoles");
        UpdatePresenters();
        await Settle();
        Capture("no-hole");
        Move(500, 50, System.Windows.Forms.Screen.PrimaryScreen!);
        await Settle();
        Capture("hole-500-50");
        var first = SnapshotMain();
        Move(750, 50, System.Windows.Forms.Screen.PrimaryScreen!);
        await Settle();
        Capture("hole-750-50");
        var second = SnapshotMain();
        var holeChecks = new
        {
            firstCenterAlpha = Alpha(first, 500, 50),
            firstSolidAlpha = Alpha(first, 750, 50),
            secondCenterAlpha = Alpha(second, 750, 50),
            secondSolidAlpha = Alpha(second, 500, 50)
        };
        if (holeChecks.firstCenterAlpha > 2 || holeChecks.secondCenterAlpha > 2 ||
            holeChecks.firstSolidAlpha < 180 || holeChecks.secondSolidAlpha < 180)
            throw new InvalidOperationException("Hole pixels did not move: " + JsonSerializer.Serialize(holeChecks));
        first = null!;
        second = null!;

        var monitorInfo = System.Windows.Forms.Screen.AllScreens.Select(s => new
        {
            s.DeviceName, s.Primary, bounds = s.Bounds.ToString(), workArea = s.WorkingArea.ToString(),
            dpi = Call("GetDpiForScreen", s)
        }).ToArray();
        var allRenderedWindows = windows.Concat(presenters.SelectMany(p =>
            (IEnumerable<Window>)p.GetType().GetProperty("Windows")!.GetValue(p)!)).ToArray();
        var windowInfo = allRenderedWindows.Select(w =>
        {
            var handle = new WindowInteropHelper(w).Handle;
            GetWindowRect(handle, out var rect);
            GetWindowDisplayAffinity(handle, out var affinity);
            return new
            {
                w.Title, w.IsVisible, w.ActualWidth, w.ActualHeight,
                dpiX = VisualTreeHelper.GetDpi(w).DpiScaleX,
                dpiY = VisualTreeHelper.GetDpi(w).DpiScaleY,
                rect, styles = GetWindowLong(handle, -20), affinity
            };
        }).ToArray();
        metadata = new
        {
            lane, duration, startedUtc = DateTime.UtcNow, pid = Environment.ProcessId,
            logicalProcessors = Environment.ProcessorCount,
            renderer = typeof(MainWindow).Assembly.Location,
            rendererHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),
            harnessHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))),
            rendererVersion = typeof(MainWindow).Assembly.GetName().Version!.ToString(),
            renderTier = RenderCapability.Tier >> 16, monitorInfo, windowInfo, holeChecks,
            cpuUnits = "one logical core: process CPU ms / wall ms * 100",
            dwmScope = "session DWM process; includes other desktop activity",
            gpu = "not sampled by harness; use external counter capture",
            limitations = "Synthetic input invokes real HandleMouseMove. Real HWNDs, main XAML, all-monitor production windows. No App startup/updater, actual cursor input or user settings writes. Snapshots render only app visuals, not desktop."
        };
        File.WriteAllText(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(metadata, JsonOptions));

        Call("ClearHoverHoles");
        UpdatePresenters();
        if (lane == "off")
        {
            Set("isLightOn", false);
            ((UIElement)typeof(MainWindow).GetField("EdgeLightBorder", Private | BindingFlags.Public)!.GetValue(main)!).Visibility = Visibility.Collapsed;
            Call("UpdateAdditionalMonitorWindows");
        }
        traceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ((DispatcherTimer)Field("cursorPollTimer")!).Interval };
        traceTimer.Tick += Trace;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (lane is not ("idle" or "off")) traceTimer.Start();
        await Task.Delay(15000);
        Console.WriteLine($"READY pid={Environment.ProcessId} monitors={monitors.Count} windows={windowInfo.Length} lane={lane}");
        ticks = 0;
        updates = 0;
        maxUpdateMs = 0;
        intervals.Clear();
        clock = Stopwatch.StartNew();
        measuredStartedUtc = DateTime.UtcNow;
        lastTick = 0;
        process.Refresh();
        lastCpu = process.TotalProcessorTime.Ticks;
        lastDwm = Cpu(dwm);
        lastSample = 0;
        startResources = Resources();
        sampleTimer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(1) };
        sampleTimer.Tick += Sample;
        sampleTimer.Start();
    }

    private static async Task Settle() { await Task.Delay(150); }
    private static long Cpu(Process? p)
    {
        try { p?.Refresh(); return p?.TotalProcessorTime.Ticks ?? -1; }
        catch (Exception ex) { dwmError = ex.Message; return -1; }
    }

    private static void Move(double x, double y, System.Windows.Forms.Screen screen)
    {
        var monitor = monitors.Single(m => m.Screen.DeviceName == screen.DeviceName);
        var timer = Stopwatch.StartNew();
        Call("HandleMouseMove", screen.WorkingArea.X + (int)Math.Round(x * monitor.DpiX),
            screen.WorkingArea.Y + (int)Math.Round(y * monitor.DpiY));
        UpdatePresenters();
        maxUpdateMs = Math.Max(maxUpdateMs, timer.Elapsed.TotalMilliseconds);
        updates++;
    }

    private static void Trace(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalMilliseconds;
        if (lastTick > 0) intervals.Add(now - lastTick);
        lastTick = now;
        ticks++;
        var t = (now / 33) % 120;
        var screen = lane == "monitors"
            ? monitors[(int)(now / 3960) % monitors.Count].Screen
            : System.Windows.Forms.Screen.PrimaryScreen!;
        var monitor = monitors.Single(m => m.Screen.DeviceName == screen.DeviceName);
        var width = screen.WorkingArea.Width / monitor.DpiX;
        var height = screen.WorkingArea.Height / monitor.DpiY;
        // 30-Hz continuous top edge sweeps; every frame has a genuinely different hole.
        var x = 200 + (width - 400) * (t < 60 ? t / 60.0 : (120 - t) / 60.0);
        var y = 50.0;
        if (lane == "monitors" && (int)(now / 1980) % 2 == 1) { x = 50; y = 200 + (height - 400) * (t % 60) / 60; }
        Move(x, y, screen);
        if (lane == "brightness") main.SetBrightness(.2 + .8 * t / 120.0, save: false);
        if (lane == "color") main.SetColorTemperature(t / 120.0, save: false);
    }

    private static void Sample(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalMilliseconds;
        var cpu = Cpu(process);
        var dwmCpu = Cpu(dwm);
        samples.Add(new
        {
            endMs = now, wallMs = now - lastSample,
            appCpuMs = (cpu - lastCpu) / 10000.0,
            appCorePercent = (cpu - lastCpu) / 100.0 / (now - lastSample),
            dwmCpuMs = dwmCpu < 0 ? (double?)null : (dwmCpu - lastDwm) / 10000.0,
            dwmCorePercent = dwmCpu < 0 ? (double?)null : (dwmCpu - lastDwm) / 100.0 / (now - lastSample),
            ticks, updates, resources = Resources()
        });
        lastCpu = cpu;
        lastDwm = dwmCpu;
        lastSample = now;
        if (now < duration * 1000) return;
        traceTimer!.Stop();
        sampleTimer!.Stop();
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            metadata, samples, tickIntervalsMs = intervals, startResources, endResources = Resources(),
            ticks, updates, maxUpdateMs, dwmError, measuredStartedUtc, measuredEndedUtc = DateTime.UtcNow
        }, JsonOptions));
        Console.WriteLine($"DONE samples={samples.Count} traceTicks={ticks} updates={updates}");
        Cleanup();
        Application.Current.Shutdown();
    }

    private static object Resources()
    {
        process.Refresh();
        return new { handles = process.HandleCount, gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1),
            privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64 };
    }

    private static RenderTargetBitmap SnapshotMain()
    {
        var dpi = VisualTreeHelper.GetDpi(main);
        var bitmap = new RenderTargetBitmap((int)Math.Round(main.ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(main.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        if (presenters.Count == 0) bitmap.Render((Visual)main.Content);
        else
        {
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var strips = (IEnumerable<Window>)presenters[0].GetType().GetProperty("Windows")!.GetValue(presenters[0])!;
                foreach (var strip in strips)
                {
                    var origin = strip.PointToScreen(new Point());
                    var mainOrigin = main.PointToScreen(new Point());
                    var stripBitmap = new RenderTargetBitmap((int)Math.Round(strip.ActualWidth * dpi.DpiScaleX),
                        (int)Math.Round(strip.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    stripBitmap.Render((Visual)strip.Content);
                    context.DrawImage(stripBitmap, new Rect((origin.X - mainOrigin.X) / dpi.DpiScaleX,
                        (origin.Y - mainOrigin.Y) / dpi.DpiScaleY, strip.ActualWidth, strip.ActualHeight));
                }
            }
            bitmap.Render(drawing);
        }
        return bitmap;
    }

    private static void UpdatePresenters()
    {
        foreach (var presenter in presenters) presenter.GetType().GetMethod("UpdateHole")!.Invoke(presenter, null);
    }

    private static byte Alpha(RenderTargetBitmap bitmap, double x, double y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(x * bitmap.DpiX / 96), (int)(y * bitmap.DpiY / 96), 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private static void Capture(string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(SnapshotMain()));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }

    private static void Cleanup()
    {
        traceTimer?.Stop();
        sampleTimer?.Stop();
        foreach (var presenter in presenters) ((IDisposable)presenter).Dispose();
        presenters.Clear();
        main?.Close();
        dwm?.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
}
