using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowsEdgeLight;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MainWindow main = null!;
    private static GpuDevice? gpu;
    private static readonly List<(Window Window, Grid Host, System.Windows.Forms.Screen Screen, double DpiX, double DpiY)> monitors = [];
    private static readonly List<GpuMonitor> surfaces = [];
    private static readonly List<object> samples = [];
    private static readonly List<double> intervals = [];
    private static readonly List<double> updateDurations = [];
    private static readonly List<object> uploadComparisons = [];
    private static readonly Dictionary<Window, Visual> visualRoots = [];
    private static readonly List<object> maskComparisons = [];
    private static readonly bool detachVisuals = Environment.GetEnvironmentVariable("GPU_DETACH_WPF") == "1";
    private static readonly string? colorCacheDirectory = Environment.GetEnvironmentVariable("GPU_COLOR_CACHE");
    private static readonly Process process = Process.GetCurrentProcess();
    private static string output = "", arm = "", lane = "";
    private static int duration, ticks;
    private static long lastCpu;
    private static double lastSample, lastTick, maxUpdateMs;
    private static Stopwatch clock = new();
    private static DispatcherTimer? traceTimer, sampleTimer;
    private static object? metadata, startResources, startGpu, startColor;
    private static DateTime measuredStartedUtc;
    private static bool closed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--apis") { ApiProbe.DumpAll(); return 0; }
        if (args.Length != 4 || !int.TryParse(args[3], out duration) || args[1] is not ("control" or "gpu"))
        {
            Console.Error.WriteLine("GpuOverlayIteration <new-output> <control|gpu> <hover|monitors|brightness|color|idle|off|color-proof|gpu-color-proof> <seconds>");
            return 2;
        }
        output = Path.GetFullPath(args[0]);
        arm = args[1];
        lane = args[2];
        if (Directory.Exists(output)) throw new IOException("Refusing to overwrite: " + output);
        Directory.CreateDirectory(output);
        using var mutex = new Mutex(true, "WindowsEdgeLight_SingleInstance_Mutex", out var owns);
        if (!owns) throw new InvalidOperationException("The app or another benchmark is running.");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Fail(e.Exception);
        };
        application.Startup += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex) { Fail(ex); }
        };
        try { return application.Run(); }
        finally { Cleanup(); mutex.ReleaseMutex(); }
    }

    private static void Fail(Exception ex)
    {
        File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString());
        Console.Error.WriteLine(ex);
        Cleanup();
        Application.Current.Shutdown(1);
    }

    private static object? Field(string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(main);
    private static void Set(string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(main, value);
    private static object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(main, args);

    private static async Task Run()
    {
        if (lane == "color-lifetime")
        {
            RunColorLifetime();
            Cleanup();
            Application.Current.Shutdown();
            return;
        }
        main = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
        Set("isLightOn", true);
        Set("isControlWindowVisible", false);
        ((System.Windows.Forms.NotifyIcon)Field("notifyIcon")!).Visible = false;
        main.Show();
        ((DispatcherTimer)Field("cursorPollTimer")!).Stop();
        main.SetBrightness(1, save: false);
        main.SetColorTemperature(.5, save: false);
        var primary = System.Windows.Forms.Screen.PrimaryScreen!;
        var primaryDpi = ((double, double))Call("GetDpiForScreen", primary)!;
        monitors.Add((main, (Grid)Field("GlowHost")!, primary, primaryDpi.Item1, primaryDpi.Item2));
        foreach (var screen in System.Windows.Forms.Screen.AllScreens.Where(s => !s.Primary))
        {
            var dpi = ((double, double))Call("GetDpiForScreen", screen)!;
            var ctx = Call("CreateMonitorWindow", screen)!;
            var window = (Window)ctx.GetType().GetProperty("Window")!.GetValue(ctx)!;
            window.ShowActivated = false;
            ((System.Collections.IList)Field("additionalMonitorWindows")!).Add(ctx);
            window.Show();
            monitors.Add((window, (Grid)window.Content, screen, dpi.Item1, dpi.Item2));
        }
        Call("UpdateAdditionalMonitorWindows");
        ((AppSettings)Field("settings")!).ExcludeFromCapture = true;
        Call("ApplyExcludeFromCapture");
        await Task.Delay(5000);
        if (((DispatcherTimer)Field("cursorPollTimer")!).IsEnabled)
            throw new InvalidOperationException("Real cursor polling is still enabled; synthetic trace would be invalid.");
        Call("ClearHoverHoles");
        foreach (var m in monitors) visualRoots.Add(m.Window, (Visual)m.Window.Content);
        if (lane == "color-proof")
        {
            foreach (var m in monitors) m.Window.Hide();
            await Task.Delay(200);
            RunColorProof();
            Cleanup();
            Application.Current.Shutdown();
            return;
        }
        var reference = Raster(monitors[0].Window);
        Save(reference, "production-no-hole");
        if (arm == "gpu")
        {
            gpu = new GpuDevice();
            foreach (var monitor in monitors)
            {
                var source = Raster(monitor.Window);
                var work = monitor.Screen.WorkingArea;
                var surface = new GpuMonitor(gpu, work.X, work.Y, source);
                surfaces.Add(surface);
                if (colorCacheDirectory != null)
                {
                    var sourceHash = ColorResponse.Hash(source);
                    var cachePath = Path.Combine(colorCacheDirectory,
                        $"cache-{source.PixelWidth}x{source.PixelHeight}-{sourceHash[..12]}.bin");
                    var cache = ColorResponse.Load(cachePath);
                    if (cache.SourceHash != sourceHash) throw new InvalidOperationException("Color cache source hash mismatch.");
                    surface.EnableColor(cache, TemperatureColor(main.GetColorTemperature()));
                }
                var comparison = Compare(source, surface.Snapshot());
                if (comparison.AlphaDifferent > 0 || comparison.MaxChannelDifference > (colorCacheDirectory == null ? 1 : 0))
                    throw new InvalidOperationException("GPU upload changed monitor pixels: " + monitor.Screen.DeviceName);
                uploadComparisons.Add(new { monitor.Screen.DeviceName, comparison });
            }
            foreach (var monitor in monitors) monitor.Window.Hide();
        }
        await Task.Delay(200);
        var noHole = Snapshot();
        Save(noHole, "no-hole");
        var equivalence = Compare(reference, noHole);
        if (equivalence.AlphaDifferent > 0 || equivalence.MaxChannelDifference > 1)
            throw new InvalidOperationException("Unmasked glow upload/readback changed pixels: " + JsonSerializer.Serialize(equivalence));
        Move(500, 50, 0);
        await Task.Delay(150);
        var first = Snapshot();
        if (surfaces.Count > 0) maskComparisons.Add(Compare(first, surfaces[0].LegacyMaskSnapshot()));
        Save(first, "hole-500-50");
        Move(750, 50, 0);
        await Task.Delay(150);
        var second = Snapshot();
        if (surfaces.Count > 0) maskComparisons.Add(Compare(second, surfaces[0].LegacyMaskSnapshot()));
        Save(second, "hole-750-50");
        var holeChecks = new
        {
            firstCenterAlpha = Alpha(first, 500, 50), firstSolidAlpha = Alpha(first, 750, 50),
            secondCenterAlpha = Alpha(second, 750, 50), secondSolidAlpha = Alpha(second, 500, 50)
        };
        if (holeChecks.firstCenterAlpha > 2 || holeChecks.secondCenterAlpha > 2 ||
            holeChecks.firstSolidAlpha < 180 || holeChecks.secondSolidAlpha < 180)
            throw new InvalidOperationException("Actual render surface failed moving-hole pixels.");
        reference = noHole = first = second = null!;
        var windowInfo = surfaces.Count > 0 ? surfaces.Select(s => s.WindowInfo()).ToArray() : monitors.Select(m =>
        {
            var handle = new WindowInteropHelper(m.Window).Handle;
            Native.GetWindowRect(handle, out var rect);
            Native.GetWindowDisplayAffinity(handle, out var affinity);
            return (object)new { rect, styles = Native.GetWindowLong(handle, -20), affinity, visible = m.Window.IsVisible };
        }).ToArray();
        metadata = new
        {
            arm, lane, duration, startedUtc = DateTime.UtcNow, pid = Environment.ProcessId,
            profile = Diagnostics.Enabled, lightweightSampler = Diagnostics.LightweightSampler,
            cachedMask = Environment.GetEnvironmentVariable("GPU_CACHE_MASK") == "1", detachVisuals, maskComparisons,
            colorCacheDirectory,
            logicalProcessors = Environment.ProcessorCount,
            renderer = typeof(MainWindow).Assembly.Location,
            rendererHash = Hash(typeof(MainWindow).Assembly.Location), harnessHash = Hash(typeof(Program).Assembly.Location),
            renderTier = RenderCapability.Tier >> 16, gpuAdapter = gpu?.Adapter,
            gpuFeatureLevel = gpu?.Device.FeatureLevel.ToString(),
            monitorInfo = monitors.Select(m => new { m.Screen.DeviceName, m.Screen.Primary,
                workArea = m.Screen.WorkingArea.ToString(), m.DpiX, m.DpiY }),
            windowInfo, holeChecks, noHolePixelComparison = equivalence, uploadComparisons,
            cpuUnits = "one logical core: process CPU ms / wall ms * 100",
            limitations = "Synthetic production input path; GPU snapshots read actual swapchain texture outside timing. No user's cursor or settings writes. Native windows disabled+transparent; WindowFromPoint checks not a physical cross-process click test."
        };
        Write("metadata.json", metadata);
        Call("ClearHoverHoles");
        UpdateGpu();
        if (lane == "gpu-color-proof")
        {
            if (arm != "gpu" || colorCacheDirectory == null)
                throw new InvalidOperationException("GPU color proof requires the GPU arm and a validated color cache.");
            RunGpuColorProof();
            Cleanup();
            Application.Current.Shutdown();
            return;
        }
        if (arm == "gpu" && detachVisuals)
            foreach (var monitor in monitors) monitor.Window.Content = null;
        if (lane == "off")
        {
            Set("isLightOn", false);
            ((UIElement)typeof(MainWindow).GetField("EdgeLightBorder", Private | BindingFlags.Public)!.GetValue(main)!).Visibility = Visibility.Collapsed;
            Call("UpdateAdditionalMonitorWindows");
            foreach (var surface in surfaces) surface.Draw(null, 0, force: true);
        }
        traceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ((DispatcherTimer)Field("cursorPollTimer")!).Interval };
        traceTimer.Tick += Trace;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        clock = Stopwatch.StartNew();
        if (lane is not ("idle" or "off")) traceTimer.Start();
        await Task.Delay(15000);
        Console.WriteLine($"READY pid={Environment.ProcessId} arm={arm} lane={lane} monitors={monitors.Count}");
        ticks = 0;
        intervals.Clear();
        updateDurations.Clear();
        maxUpdateMs = lastTick = lastSample = 0;
        Diagnostics.Start();
        clock.Restart();
        measuredStartedUtc = DateTime.UtcNow;
        lastCpu = Cpu();
        startResources = Resources();
        startGpu = surfaces.Select(s => s.Counters()).ToArray();
        startColor = surfaces.Select(s => s.ColorCounters()).ToArray();
        sampleTimer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(1) };
        sampleTimer.Tick += Sample;
        sampleTimer.Start();
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Color TemperatureColor(double temperature) =>
        (Color)typeof(MainWindow).GetMethod("GetColorForTemperature", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [temperature])!;

    private static void SetGray(Color color)
    {
        foreach (var monitor in monitors)
        {
            var cached = (Grid)monitor.Host.Children[0];
            var path = (System.Windows.Shapes.Path)cached.Children[0];
            foreach (var stop in ((LinearGradientBrush)path.Fill).GradientStops)
                if (stop.Offset is > .2 and < .8) stop.Color = color;
            ((System.Windows.Media.Effects.DropShadowEffect)path.Effect).Color = color;
        }
    }

    private static void RunColorProof()
    {
        var reports = new List<object>();
        var cacheIndex = new Dictionary<string, string>();
        double[] temperatures = [0, .125, .25, .37, .5, .75, .875, 1, .4 - 1e-8, .4 + 1e-8];
        foreach (var monitor in monitors)
        {
            main.SetColorTemperature(.5, save: false);
            var baseline = Raster(monitor.Window);
            var hash = ColorResponse.Hash(baseline);
            if (!cacheIndex.TryGetValue(hash, out var cacheName))
            {
                cacheName = $"cache-{baseline.PixelWidth}x{baseline.PixelHeight}-{hash[..12]}.bin";
                SetGray(Colors.Black); var black = ColorResponse.Pixels(Raster(monitor.Window));
                SetGray(Colors.White); var white = ColorResponse.Pixels(Raster(monitor.Window));
                var linear = new List<object>();
                foreach (var temperature in temperatures)
                {
                    main.SetColorTemperature(temperature, save: false);
                    var target = ColorResponse.Pixels(Raster(monitor.Window));
                    linear.Add(new { temperature, color = TemperatureColor(temperature).ToString(),
                        difference = ColorResponse.Difference(target, ColorResponse.LinearBasis(black, white, TemperatureColor(temperature))) });
                }
                Write("linear-basis-" + hash[..12] + ".json", linear);
                var cache = ColorResponse.Build(baseline, color => { SetGray(color); return Raster(monitor.Window); },
                    Path.Combine(output, "cache-build-" + hash[..12] + ".json"));
                cache.Save(Path.Combine(output, cacheName));
                cacheIndex.Add(hash, cacheName);
            }
            var loaded = ColorResponse.Load(Path.Combine(output, cacheName));
            var checks = new List<object>();
            foreach (var temperature in temperatures)
            {
                main.SetColorTemperature(temperature, save: false);
                var expected = ColorResponse.Pixels(Raster(monitor.Window));
                var evaluated = loaded.Evaluate(TemperatureColor(temperature));
                var difference = ColorResponse.Difference(expected, evaluated);
                checks.Add(new { temperature, color = TemperatureColor(temperature).ToString(), difference });
                if (!expected.AsSpan().SequenceEqual(evaluated))
                {
                    Write("color-proof-failed.json", new { monitor.Screen.DeviceName, checks });
                    throw new InvalidOperationException("Exact response lookup does not match production; refusing approximate color.");
                }
            }
            reports.Add(new { monitor.Screen.DeviceName, hash, cacheName, loaded.Classes, checks });
            Write("color-proof.json", reports);
        }
        main.SetColorTemperature(.5, save: false);
        Console.WriteLine("COLOR-PROOF passed all production temperature/monitor comparisons.");
    }

    private static void RunGpuColorProof()
    {
        var checks = new List<object>();
        double[] temperatures = [0, .125, .25, .37, .5, .75, .875, 1, .4 - 1e-8, .4 + 1e-8];
        foreach (var temperature in temperatures)
        {
            main.SetColorTemperature(temperature, save: false);
            var timer = Stopwatch.StartNew();
            UpdateGpu(force: true);
            var submittedMs = timer.Elapsed.TotalMilliseconds;
            for (var i = 0; i < surfaces.Count; i++)
            {
                var actual = surfaces[i].Snapshot();
                var expected = Raster(monitors[i].Window);
                var difference = Compare(expected, actual);
                checks.Add(new { temperature, color = TemperatureColor(temperature).ToString(),
                    monitors[i].Screen.DeviceName, submittedMs, difference, counters = surfaces[i].ColorCounters() });
                Write("gpu-color-proof.json", checks);
                if (difference.MaxChannelDifference != 0)
                    throw new InvalidOperationException("Actual GPU color readback is not byte-exact with production.");
            }
        }
        var opaque = Raster(main);
        var brightnessChecks = new List<object>();
        foreach (var brightness in new[] { .2, .5, .82, 1.0 })
        {
            main.SetBrightness(brightness, save: false);
            Move(500, 50, 0);
            var actual = surfaces[0].Snapshot();
            var nativeReference = surfaces[0].ReferenceColorSnapshot(opaque);
            var colorDifference = Compare(nativeReference, actual);
            brightnessChecks.Add(new { brightness, colorDifference,
                productionDifference = Compare(Raster(main), actual),
                centerAlpha = Alpha(actual, 500, 50), solidAlpha = Alpha(actual, 750, 50) });
            Write("gpu-brightness-proof.json", brightnessChecks);
            if (colorDifference.MaxChannelDifference != 0 || Alpha(actual, 500, 50) > 2)
                throw new InvalidOperationException("GPU color lookup changed native brightness/moving-hole output.");
        }
        Console.WriteLine("GPU-COLOR-PROOF passed actual GPU temperatures and native brightness/hole equivalence.");
    }

    private static void VerifyFinalColor()
    {
        if (arm != "gpu" || colorCacheDirectory == null || lane != "color") return;
        var temperature = main.GetColorTemperature();
        var color = TemperatureColor(temperature);
        var countersBeforeVerification = surfaces.Select(s => s.ColorCounters()).ToArray();
        Call("ClearHoverHoles");
        UpdateGpu(force: true);
        var checks = new List<object>();
        for (var i = 0; i < surfaces.Count; i++)
        {
            var actual = surfaces[i].Snapshot();
            var expected = Raster(monitors[i].Window);
            var difference = Compare(expected, actual);
            checks.Add(new { monitors[i].Screen.DeviceName, difference });
            Write("final-color-proof.json", new { temperature, color = color.ToString(), countersBeforeVerification, checks });
            if (difference.MaxChannelDifference != 0)
                throw new InvalidOperationException("Trailing requested color is not exact on the actual GPU surface.");
        }
    }

    private static void RunColorLifetime()
    {
        if (arm != "gpu" || colorCacheDirectory == null || duration is < 1 or > 10)
            throw new InvalidOperationException("Color lifetime requires GPU/cache and 1-10 cycles.");
        var screens = System.Windows.Forms.Screen.AllScreens;
        var caches = Directory.GetFiles(colorCacheDirectory, "cache-*.bin").Select(ColorResponse.Load).ToArray();
        var rows = new List<object>();
        var baseline = NativeProcess.Resources();
        for (var cycle = 0; cycle < duration; cycle++)
        {
            var checks = new List<object>();
            using (var device = new GpuDevice())
            {
                foreach (var screen in screens)
                {
                    var area = screen.WorkingArea;
                    var cache = caches.Single(c => c.Width == area.Width && c.Height == area.Height);
                    var color = TemperatureColor(.5);
                    var pixels = cache.Evaluate(color);
                    var source = BitmapSource.Create(cache.Width, cache.Height, cache.DpiX, cache.DpiY,
                        PixelFormats.Pbgra32, null, pixels, cache.Width * 4);
                    using var surface = new GpuMonitor(device, area.X, area.Y, source);
                    if (Native.GetDpiForWindow(surface.Handle) != cache.DpiX)
                        throw new InvalidOperationException("Lifetime cache DPI no longer matches the display.");
                    surface.EnableColor(cache, color);
                    foreach (var temperature in new[] { 0, .37, 1, .5 })
                    {
                        surface.UpdateColor(TemperatureColor(temperature));
                        surface.Draw(new Point(500, 50), .5f);
                    }
                    surface.Draw(null, 1, force: true);
                    var difference = Compare(source, surface.Snapshot());
                    if (difference.MaxChannelDifference != 0)
                        throw new InvalidOperationException("Lifetime cycle produced wrong GPU color pixels.");
                    checks.Add(new { screen.DeviceName, difference, window = surface.WindowInfo() });
                }
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            rows.Add(new { cycle = cycle + 1, resources = NativeProcess.Resources(), checks });
            Write("lifetime.json", new { baseline, rows, harnessHash = Hash(typeof(Program).Assembly.Location),
                rendererHash = Hash(typeof(MainWindow).Assembly.Location),
                limitation = "Native color resource recreation only; not production hotplug/toggle lifecycle integration." });
        }
        Console.WriteLine($"COLOR-LIFETIME passed {duration} cycles across {screens.Length} monitors.");
    }

    private static long Cpu()
    {
        if (Diagnostics.LightweightSampler) return NativeProcess.Cpu();
        process.Refresh();
        return process.TotalProcessorTime.Ticks;
    }

    private static void Move(double x, double y, int monitorIndex)
    {
        var m = monitors[monitorIndex];
        using (Diagnostics.Measure(Phase.Input))
            Call("HandleMouseMove", m.Screen.WorkingArea.X + (int)Math.Round(x * m.DpiX),
                m.Screen.WorkingArea.Y + (int)Math.Round(y * m.DpiY));
        UpdateGpu();
    }

    private static void UpdateGpu(bool force = false)
    {
        Color? color = surfaces.Count == 0 || colorCacheDirectory == null ? null : TemperatureColor(main.GetColorTemperature());
        for (var i = 0; i < surfaces.Count; i++)
        {
            if (color.HasValue) surfaces[i].UpdateColor(color.Value);
            var hole = (monitors[i].Host.Clip as CombinedGeometry)?.Geometry2 as EllipseGeometry;
            surfaces[i].Draw(hole?.Center, (float)main.GetBrightness(), force);
        }
    }

    private static void Trace(object? sender, EventArgs e)
    {
        var timer = Stopwatch.StartNew();
        var now = clock.Elapsed.TotalMilliseconds;
        if (lastTick > 0) intervals.Add(now - lastTick);
        lastTick = now;
        ticks++;
        var t = (now / 33) % 120;
        var index = lane == "monitors" ? (int)(now / 3960) % monitors.Count : 0;
        var m = monitors[index];
        var width = m.Screen.WorkingArea.Width / m.DpiX;
        var height = m.Screen.WorkingArea.Height / m.DpiY;
        var x = 200 + (width - 400) * (t < 60 ? t / 60 : (120 - t) / 60);
        var y = 50.0;
        if (lane == "monitors" && (int)(now / 1980) % 2 == 1) { x = 50; y = 200 + (height - 400) * (t % 60) / 60; }
        if (lane == "brightness") main.SetBrightness(.2 + .8 * t / 120, save: false);
        if (lane == "color")
        {
            main.SetColorTemperature(t / 120, save: false);
            for (var i = 0; colorCacheDirectory == null && i < surfaces.Count; i++)
            {
                var host = monitors[i].Host;
                var clip = host.Clip;
                host.Clip = null;
                try
                {
                    var source = Raster(monitors[i].Window);
                    if (Alpha(source, 500, 50) < 180)
                        throw new InvalidOperationException("Color rerasterization lost the visible production glow.");
                    surfaces[i].Upload(source);
                }
                finally { host.Clip = clip; }
            }
        }
        Move(x, y, index);
        if (lane == "color" && colorCacheDirectory == null) UpdateGpu(force: true);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        updateDurations.Add(elapsed);
        maxUpdateMs = Math.Max(maxUpdateMs, elapsed);
    }

    private static void Sample(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalMilliseconds;
        var cpu = Cpu();
        samples.Add(new
        {
            endMs = now, wallMs = now - lastSample, appCpuMs = (cpu - lastCpu) / 10000.0,
            appCorePercent = (cpu - lastCpu) / 100.0 / (now - lastSample), ticks,
            resources = Resources(), gpu = surfaces.Select(s => s.Counters()).ToArray()
        });
        lastCpu = cpu;
        lastSample = now;
        Diagnostics.Sample();
        if (now < duration * 1000) return;
        traceTimer!.Stop();
        sampleTimer!.Stop();
        Write("result.json", new
        {
            metadata, samples, tickIntervalsMs = intervals, updateDurationsMs = updateDurations,
            ticks, updates = ticks, maxUpdateMs,
            startResources, endResources = Resources(), startGpu,
            endGpu = surfaces.Select(s => s.Counters()).ToArray(),
            startColor, endColor = surfaces.Select(s => s.ColorCounters()).ToArray(),
            diagnostics = Diagnostics.Result(),
            measuredStartedUtc, measuredEndedUtc = DateTime.UtcNow
        });
        Save(Snapshot(), "final-frame");
        Save(Raster(main), "final-production-reference");
        VerifyFinalColor();
        Console.WriteLine($"DONE arm={arm} samples={samples.Count} traceTicks={ticks}");
        Cleanup();
        Application.Current.Shutdown();
    }

    private static object Resources()
    {
        using var measured = Diagnostics.Measure(Phase.ResourceSample);
        if (Diagnostics.LightweightSampler) return NativeProcess.Resources();
        process.Refresh();
        return new { handles = process.HandleCount, gdi = Native.GetGuiResources(process.Handle, 0),
            user = Native.GetGuiResources(process.Handle, 1), privateBytes = process.PrivateMemorySize64,
            workingSetBytes = process.WorkingSet64 };
    }

    private static BitmapSource Raster(Window window)
    {
        var detached = window.Content == null;
        if (detached) window.Content = visualRoots[window];
        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Round(window.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(window.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render((Visual)window.Content!);
            return bitmap;
        }
        finally { if (detached) window.Content = null; }
    }

    private static BitmapSource Snapshot() => surfaces.Count > 0 ? surfaces[0].Snapshot() : Raster(main);
    private static void Save(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }

    private static byte Alpha(BitmapSource bitmap, double x, double y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(x * bitmap.DpiX / 96), (int)(y * bitmap.DpiY / 96), 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private sealed record PixelDifference(int MaxChannelDifference, int AlphaDifferent, int PixelsDifferent);

    private static PixelDifference Compare(BitmapSource first, BitmapSource second)
    {
        var length = first.PixelWidth * first.PixelHeight * 4;
        var a = new byte[length];
        var b = new byte[length];
        first.CopyPixels(a, first.PixelWidth * 4, 0);
        second.CopyPixels(b, second.PixelWidth * 4, 0);
        var max = 0; var alphaDifferent = 0; var pixelsDifferent = 0;
        for (var i = 0; i < length; i += 4)
        {
            var different = false;
            for (var j = 0; j < 4; j++)
            {
                var delta = Math.Abs(a[i + j] - b[i + j]);
                max = Math.Max(max, delta);
                different |= delta != 0;
            }
            if (a[i + 3] != b[i + 3]) alphaDifferent++;
            if (different) pixelsDifferent++;
        }
        return new PixelDifference(max, alphaDifferent, pixelsDifferent);
    }

    private static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));

    private static void Cleanup()
    {
        if (closed) return;
        closed = true;
        traceTimer?.Stop();
        sampleTimer?.Stop();
        Diagnostics.Close();
        foreach (var surface in surfaces) surface.Dispose();
        surfaces.Clear();
        gpu?.Dispose();
        main?.Close();
    }
}
