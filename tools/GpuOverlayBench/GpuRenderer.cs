using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using D3DMapFlags = Vortice.Direct3D11.MapFlags;
using Point = System.Windows.Point;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using InterpolationMode = Vortice.Direct2D1.InterpolationMode;

internal sealed class GpuDevice : IDisposable
{
    public ID3D11Device Device { get; private set; } = null!;
    public ID3D11DeviceContext Immediate { get; private set; } = null!;
    public ID2D1Factory1 Factory { get; private set; } = null!;
    public ID2D1Device D2DDevice { get; private set; } = null!;
    public IDXGIFactory2 DxgiFactory { get; private set; } = null!;
    public IDCompositionDevice Composition { get; private set; } = null!;
    public string Adapter { get; private set; } = "";

    public GpuDevice()
    {
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext immediate).CheckError();
            Device = device;
            Immediate = immediate;
            using var dxgi = Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgi.GetAdapter();
            Adapter = adapter.Description.Description;
            DxgiFactory = adapter.GetParent<IDXGIFactory2>();
            Factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            D2DDevice = Factory.CreateDevice(dxgi);
            Composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        Composition?.Dispose();
        D2DDevice?.Dispose();
        Factory?.Dispose();
        DxgiFactory?.Dispose();
        Immediate?.ClearState();
        Immediate?.Flush();
        Immediate?.Dispose();
        Device?.Dispose();
    }
}

internal sealed class GpuMonitor : IDisposable
{
    private readonly GpuDevice gpu;
    private OverlayWindow window = null!;
    private IDXGISwapChain1 swapchain = null!;
    private ID3D11Texture2D backbuffer = null!;
    private ID2D1DeviceContext context = null!;
    private ID2D1Bitmap1 targetBitmap = null!;
    private ID2D1Bitmap1? glow;
    private IDCompositionTarget target = null!;
    private IDCompositionVisual visual = null!;
    private ID2D1RectangleGeometry bounds = null!;
    private ID2D1Layer layer = null!;
    private Point? lastHole;
    private float lastOpacity = 1;
    private bool disposed;
    public int Width { get; }
    public int Height { get; }
    public double DpiX { get; }
    public double DpiY { get; }
    public IntPtr Handle => window.Handle;
    public long UploadBytes { get; private set; }
    public int Uploads { get; private set; }
    public int Draws { get; private set; }
    public int Presents { get; private set; }
    public int Readbacks { get; private set; }

    public GpuMonitor(GpuDevice gpu, int x, int y, BitmapSource source)
    {
        this.gpu = gpu;
        Width = source.PixelWidth;
        Height = source.PixelHeight;
        DpiX = source.DpiX / 96;
        DpiY = source.DpiY / 96;
        try
        {
            window = new OverlayWindow(x, y, Width, Height);
            if (!Native.SetWindowDisplayAffinity(Handle, 0x11))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetWindowDisplayAffinity");
            swapchain = gpu.DxgiFactory.CreateSwapChainForComposition(gpu.Device, new SwapChainDescription1
            {
                Width = (uint)Width, Height = (uint)Height, Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied
            }, null!);
            backbuffer = swapchain.GetBuffer<ID3D11Texture2D>(0);
            context = gpu.D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
            using (var surface = backbuffer.QueryInterface<IDXGISurface>())
                targetBitmap = context.CreateBitmapFromDxgiSurface(surface,
                    new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied),
                        96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            context.Target = targetBitmap;
            context.SetDpi(96, 96);
            bounds = gpu.Factory.CreateRectangleGeometry(new RawRectF(0, 0, Width, Height));
            layer = context.CreateLayer();
            gpu.Composition.CreateTargetForHwnd(Handle, true, out target).CheckError();
            visual = gpu.Composition.CreateVisual();
            visual.SetContent(swapchain).CheckError();
            target.SetRoot(visual).CheckError();
            gpu.Composition.Commit().CheckError();
            Upload(source);
            Draw(null, 1, force: true);
        }
        catch { Dispose(); throw; }
    }

    public unsafe void Upload(BitmapSource source)
    {
        if (source.PixelWidth != Width || source.PixelHeight != Height || source.Format != PixelFormats.Pbgra32)
            throw new InvalidOperationException("GPU source must be matching native-DPI premultiplied BGRA.");
        var pixels = new byte[Width * Height * 4];
        source.CopyPixels(pixels, Width * 4, 0);
        ID2D1Bitmap1 replacement;
        fixed (byte* pointer = pixels)
            replacement = context.CreateBitmap(new SizeI(Width, Height), (IntPtr)pointer, (uint)(Width * 4),
                new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96));
        glow?.Dispose();
        glow = replacement;
        Uploads++;
        UploadBytes += pixels.Length;
    }

    public void Draw(Point? hole, float opacity, bool force = false)
    {
        if (!force && hole == lastHole && opacity == lastOpacity) return;
        lastHole = hole;
        lastOpacity = opacity;
        Render();
        swapchain.Present(0, PresentFlags.None).CheckError();
        Presents++;
    }

    private void Render()
    {
        using var mask = lastHole.HasValue ? gpu.Factory.CreatePathGeometry() : null;
        if (lastHole is { } hole)
        {
            using var ellipse = gpu.Factory.CreateEllipseGeometry(new Ellipse(
                new Vector2((float)(hole.X * DpiX), (float)(hole.Y * DpiY)),
                (float)(70 * DpiX), (float)(70 * DpiY)));
            using var sink = mask!.Open();
            bounds.CombineWithGeometry(ellipse, CombineMode.Exclude, sink);
            sink.Close();
        }
        context.BeginDraw();
        context.Clear(new Color4(0, 0, 0, 0));
        if (mask != null)
            context.PushLayer(new LayerParameters1
            {
                ContentBounds = new RawRectF(0, 0, Width, Height), GeometricMask = mask,
                MaskAntialiasMode = AntialiasMode.PerPrimitive, MaskTransform = Matrix3x2.Identity,
                Opacity = 1, LayerOptions = LayerOptions1.None
            }, layer);
        context.DrawBitmap(glow!, lastOpacity, InterpolationMode.NearestNeighbor);
        if (mask != null) context.PopLayer();
        context.EndDraw().CheckError();
        Draws++;
    }

    // Readback is an untimed verification path, never used for hover updates.
    public BitmapSource Snapshot()
    {
        Render();
        var description = backbuffer.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = gpu.Device.CreateTexture2D(description);
        gpu.Immediate.CopyResource(staging, backbuffer);
        var map = gpu.Immediate.Map(staging, 0, MapMode.Read, D3DMapFlags.None);
        var bytes = new byte[Width * Height * 4];
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(map.DataPointer + (int)(y * map.RowPitch), bytes, y * Width * 4, Width * 4);
        }
        finally { gpu.Immediate.Unmap(staging, 0); }
        swapchain.Present(0, PresentFlags.None).CheckError();
        Presents++;
        Readbacks++;
        return BitmapSource.Create(Width, Height, 96 * DpiX, 96 * DpiY, PixelFormats.Pbgra32, null, bytes, Width * 4);
    }

    public object Counters() => new { Uploads, UploadBytes, Draws, Presents, Readbacks };

    public object WindowInfo()
    {
        Native.GetWindowRect(Handle, out var rect);
        Native.GetWindowDisplayAffinity(Handle, out var affinity);
        var edgeHit = Native.WindowFromPoint(new Native.NativePoint { X = rect.Left + 100, Y = rect.Top + 75 });
        var centerHit = Native.WindowFromPoint(new Native.NativePoint { X = rect.Left + Width / 2, Y = rect.Top + Height / 2 });
        if (edgeHit == Handle || centerHit == Handle)
            throw new InvalidOperationException("Disabled composition window intercepted WindowFromPoint hit testing.");
        return new
        {
            rect, styles = Native.GetWindowLong(Handle, -20), windowStyles = Native.GetWindowLong(Handle, -16),
            affinity, dpi = Native.GetDpiForWindow(Handle), visible = Native.IsWindowVisible(Handle),
            enabled = Native.IsWindowEnabled(Handle), edgeHitThrough = edgeHit != Handle, centerHitThrough = centerHit != Handle
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (target != null) target.SetRoot(null!);
        if (visual != null) visual.SetContent(null!);
        gpu.Composition?.Commit();
        if (context != null) context.Target = null!;
        layer?.Dispose();
        bounds?.Dispose();
        glow?.Dispose();
        targetBitmap?.Dispose();
        context?.Dispose();
        backbuffer?.Dispose();
        visual?.Dispose();
        target?.Dispose();
        swapchain?.Dispose();
        window?.Dispose();
    }

    private sealed class OverlayWindow : System.Windows.Forms.NativeWindow, IDisposable
    {
        public OverlayWindow(int x, int y, int width, int height)
        {
            CreateHandle(new System.Windows.Forms.CreateParams
            {
                Caption = "Edge light GPU experiment",
                X = x, Y = y, Width = width, Height = height,
                Style = unchecked((int)0x98000000), // popup, visible, disabled: the overlay is never an input target
                ExStyle = 0x082000A8 // noactivate, noredirectionbitmap, toolwindow, transparent, topmost
            });
        }

        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x14) { message.Result = new IntPtr(1); return; }
            base.WndProc(ref message);
        }

        public void Dispose() => DestroyHandle();
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] internal static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int GetGuiResources(IntPtr process, int flags);
}
