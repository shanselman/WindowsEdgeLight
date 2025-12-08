using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.DirectComposition;

namespace WindowsEdgeLight;

/// <summary>
/// Creates a native HDR overlay window that renders the edge light effect using Direct3D11
/// with scRGB color space for true HDR brightness above SDR white.
/// 
/// Technical approach:
/// - Uses DirectComposition for per-pixel alpha transparency (WPF can't do this with HDR)
/// - scRGB color space (R16G16B16A16_Float) allows values > 1.0 for HDR brightness
/// - Premultiplied alpha blending for correct compositing with desktop
/// - Click-through via WS_EX_TRANSPARENT | WS_EX_LAYERED window styles
/// 
/// The shader renders a frame shape matching WPF's geometry:
/// - Outer rounded rect inset by Margin from screen edge
/// - Inner rounded rect creating the frame hole
/// - Inner glow extending inside (like WPF's DropShadowEffect BlurRadius)
/// - Outer glow for soft edge
/// </summary>
public class HdrOverlayWindow : IDisposable
{
    // Win32 Window handling
    private IntPtr _hwnd;
    private bool _isVisible;
    private int _width;
    private int _height;
    private int _left;
    private int _top;
    
    // D3D11 resources
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11BlendState? _blendState;
    
    // DirectComposition resources for transparency
    // Required because HWND swap chains can't do per-pixel alpha with flip model
    private IDCompositionDevice? _compositionDevice;
    private IDCompositionTarget? _compositionTarget;
    private IDCompositionVisual? _compositionVisual;
    
    private bool _disposed;
    private bool _isInitialized;
    private Thread? _renderThread;
    private bool _stopRenderThread;
    
    // HDR settings - values in PHYSICAL pixels
    // These defaults assume 100% DPI (1.0 scale) - MainWindow will override with actual DPI-scaled values
    // WPF base values: frameThickness=80, outerRadius=100, innerRadius=60, margin=20
    public float HdrBrightness { get; set; } = 6.0f;    // 6x SDR white = ~480 nits (reasonable default)
    public float ColorTemperature { get; set; } = 0.5f; // 0=cool, 1=warm
    public float EdgeThickness { get; set; } = 80.0f;   // Will be multiplied by DPI scale
    public float CornerRadius { get; set; } = 100.0f;   // Outer corner radius - will be multiplied by DPI scale
    public float InnerCornerRadius { get; set; } = 60.0f; // Inner corner radius - will be multiplied by DPI scale
    public float Margin { get; set; } = 20.0f;          // Will be multiplied by DPI scale
    
    public const float MaxHdrBrightness = 20.0f;  // ~1600 nits
    public const float MinHdrBrightness = 1.0f;   // SDR white (~80 nits)
    
    // Native window styles
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x8000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000; // Required for DWM composition with D3D
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    
    private const int GWL_EXSTYLE = -20;
    private const int HWND_TOPMOST = -1;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    
    // DWM API for transparency
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }
    
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
    
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct EdgeLightConstants
    {
        public float Width;
        public float Height;
        public float EdgeThickness;
        public float CornerRadius;       // Outer corner radius
        public float InnerCornerRadius;  // Inner corner radius (WPF uses 60 vs outer 100)
        public float HdrBrightness;
        public float ColorTemperature;
        public float Margin;             // Offset from screen edge
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public System.Numerics.Vector2 Position;
        public System.Numerics.Vector2 TexCoord;
    }

    // Pixel shader that renders edge light with HDR brightness
    // Matches WPF geometry: a frame (outer rect - inner rect) with gradient fill and glow
    // Outputs in scRGB linear format (R16G16B16A16_FLOAT) with premultiplied alpha
    private const string PixelShaderSource = @"
cbuffer Constants : register(b0) {
    float Width;
    float Height;
    float EdgeThickness;      // Frame thickness in physical pixels
    float CornerRadius;       // Outer corner radius in physical pixels
    float InnerCornerRadius;  // Inner corner radius (WPF uses 60 vs outer 100)
    float HdrBrightness;
    float ColorTemperature;
    float Margin;             // Offset from screen edge in physical pixels
};

struct PS_INPUT {
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

// Signed distance to rounded rectangle (negative inside, positive outside)
float sdRoundedBox(float2 p, float2 b, float r) {
    float2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float4 main(PS_INPUT input) : SV_TARGET {
    float2 uv = input.TexCoord;
    float2 pixelPos = uv * float2(Width, Height);
    
    // Center coordinates for SDF
    float2 center = float2(Width, Height) * 0.5;
    float2 p = pixelPos - center;
    
    // Outer rectangle parameters - inset by Margin from screen edge
    float2 outerSize = float2(Width, Height) * 0.5 - Margin;
    float outerRadius = CornerRadius;
    
    // Inner rectangle parameters (outer minus frame thickness)
    float2 innerSize = outerSize - EdgeThickness;
    // Use explicit inner radius from WPF (60 at 100% DPI)
    float innerRadius = InnerCornerRadius;
    
    // Signed distances
    float distOuter = sdRoundedBox(p, outerSize, outerRadius);
    float distInner = sdRoundedBox(p, innerSize, innerRadius);
    
    // Glow extends beyond the frame - like WPF's DropShadowEffect BlurRadius=76
    float glowExtent = 76.0; // Match WPF BlurRadius
    
    // We render if we're in the frame OR in the glow zone
    bool inFrame = (distOuter < 0) && (distInner > 0);
    bool inInnerGlow = (distInner < 0) && (distInner > -glowExtent); // Inside inner rect (glow toward screen center)
    bool inOuterGlow = (distOuter > 0) && (distOuter < glowExtent);  // Outside outer rect (glow toward screen edge)
    
    if (!inFrame && !inInnerGlow && !inOuterGlow) {
        return float4(0, 0, 0, 0);
    }
    
    // Calculate glow intensity
    float glowFactor = 0.0;
    
    if (inFrame) {
        // In the solid frame - uniform full brightness (like WPF's solid fill)
        glowFactor = 1.0;
    }
    else if (inInnerGlow) {
        // Inner glow - fades as we go deeper toward screen center
        float glowDist = -distInner; // Distance from inner edge going inward
        glowFactor = 1.0 - (glowDist / glowExtent);
        glowFactor = pow(glowFactor, 1.2); // Soft falloff like blur
    }
    else if (inOuterGlow) {
        // Outer glow - fades as we go toward screen edge
        float glowDist = distOuter; // Distance from outer edge going outward
        glowFactor = 1.0 - (glowDist / glowExtent);
        glowFactor = pow(glowFactor, 1.2); // Soft falloff like blur
    }
    
    glowFactor = saturate(glowFactor);
    
    // Color temperature blend (cool blue-white to warm amber-white)
    float3 coolWhite = float3(0.9, 0.95, 1.0);
    float3 warmWhite = float3(1.0, 0.92, 0.85);
    float3 baseColor = lerp(coolWhite, warmWhite, ColorTemperature);
    
    // Apply HDR brightness in scRGB linear space
    float3 hdrColor = baseColor * HdrBrightness * glowFactor;
    
    // Alpha based on glow factor
    float alpha = glowFactor;
    
    // Output premultiplied alpha
    return float4(hdrColor * alpha, alpha);
}
";

    private const string VertexShaderSource = @"
struct VS_INPUT {
    float2 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT {
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT main(VS_INPUT input) {
    VS_OUTPUT output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}
";

    public bool Initialize(int x, int y, int width, int height)
    {
        return Initialize(x, y, width, height, out _);
    }
    
    public bool Initialize(int x, int y, int width, int height, out string? errorMessage)
    {
        errorMessage = null;
        if (_isInitialized) return true;
        
        try
        {
            _left = x;
            _top = y;
            _width = width;
            _height = height;
            
            // Create a popup window with WS_EX_NOREDIRECTIONBITMAP for DWM composition
            // WS_EX_TRANSPARENT + WS_EX_LAYERED enables click-through
            _hwnd = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_LAYERED,
                "Static", // Using existing window class
                "HDR Edge Light Overlay",
                WS_POPUP,
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            
            if (_hwnd == IntPtr.Zero)
            {
                errorMessage = $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}";
                return false;
            }
            
            // Extend DWM frame for transparency support
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            
            // Initialize Direct3D
            if (!InitializeD3D(out errorMessage))
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
                return false;
            }
            
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Exception: {ex.Message}";
            Dispose();
            return false;
        }
    }
    
    private bool InitializeD3D(out string? errorMessage)
    {
        errorMessage = null;
        
        try
        {
            // Create D3D11 device
            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out _device,
                out _context);
            
            if (_device == null || _context == null)
            {
                errorMessage = "D3D11CreateDevice failed - device or context is null";
                return false;
            }
            
            // Get DXGI factory and device for composition
            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            
            // Create DirectComposition device for per-pixel transparency
            DComp.DCompositionCreateDevice(dxgiDevice, out _compositionDevice);
            if (_compositionDevice == null)
            {
                errorMessage = "Failed to create DirectComposition device";
                return false;
            }
            
            // Create swap chain for composition (not for HWND directly)
            // This allows premultiplied alpha transparency
            var swapChainDesc = new SwapChainDescription1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = Format.R16G16B16A16_Float, // scRGB HDR format
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied, // Enable per-pixel transparency
                Flags = SwapChainFlags.None
            };
            
            // Use CreateSwapChainForComposition instead of ForHwnd
            _swapChain = factory.CreateSwapChainForComposition(_device, swapChainDesc);
            
            // Set up DirectComposition visual tree
            _compositionDevice.CreateTargetForHwnd(_hwnd, true, out _compositionTarget);
            _compositionVisual = _compositionDevice.CreateVisual();
            _compositionVisual.SetContent(_swapChain);
            _compositionTarget!.SetRoot(_compositionVisual);
            _compositionDevice.Commit();
            
            // Set scRGB color space for HDR with linear gamma
            try
            {
                using var swapChain3 = _swapChain.QueryInterface<IDXGISwapChain3>();
                // scRGB: linear gamma, full range, sRGB primaries - allows values > 1.0 for HDR
                swapChain3.SetColorSpace1(ColorSpaceType.RgbFullG10NoneP709);
            }
            catch
            {
                // Continue without explicit color space if not supported
            }
            
            // Create render target view
            CreateRenderTargetView();
            
            // Compile shaders
            if (!CreateShaders(out errorMessage))
            {
                return false;
            }
            
            // Create constant buffer
            CreateConstantBuffer();
            
            // Create vertex buffer
            CreateVertexBuffer();
            
            // Create blend state for alpha blending
            CreateBlendState();
            
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"InitializeD3D exception: {ex.Message}";
            return false;
        }
    }
    
    private void CreateRenderTargetView()
    {
        using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device!.CreateRenderTargetView(backBuffer);
    }
    
    private bool CreateShaders(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            Compiler.Compile(VertexShaderSource, "main", string.Empty, "vs_5_0", out var vsBlob, out var vsErrors);
            if (vsBlob == null)
            {
                errorMessage = $"Vertex shader compile failed: {vsErrors?.AsString() ?? "unknown error"}";
                return false;
            }
            
            _vertexShader = _device!.CreateVertexShader(vsBlob.AsBytes());
            
            var inputElements = new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0)
            };
            _inputLayout = _device.CreateInputLayout(inputElements, vsBlob.AsBytes());
            vsBlob.Dispose();
            
            Compiler.Compile(PixelShaderSource, "main", string.Empty, "ps_5_0", out var psBlob, out var psErrors);
            if (psBlob == null)
            {
                errorMessage = $"Pixel shader compile failed: {psErrors?.AsString() ?? "unknown error"}";
                return false;
            }
            
            _pixelShader = _device.CreatePixelShader(psBlob.AsBytes());
            psBlob.Dispose();
            
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"CreateShaders exception: {ex.Message}";
            return false;
        }
    }
    
    private void CreateConstantBuffer()
    {
        var bufferDesc = new BufferDescription
        {
            ByteWidth = (uint)((Marshal.SizeOf<EdgeLightConstants>() + 15) & ~15),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        };
        _constantBuffer = _device!.CreateBuffer(bufferDesc);
    }
    
    private void CreateVertexBuffer()
    {
        var vertices = new Vertex[]
        {
            new() { Position = new(-1, -1), TexCoord = new(0, 1) },
            new() { Position = new(-1,  1), TexCoord = new(0, 0) },
            new() { Position = new( 1,  1), TexCoord = new(1, 0) },
            new() { Position = new(-1, -1), TexCoord = new(0, 1) },
            new() { Position = new( 1,  1), TexCoord = new(1, 0) },
            new() { Position = new( 1, -1), TexCoord = new(1, 1) },
        };
        
        _vertexBuffer = _device!.CreateBuffer(vertices, new BufferDescription
        {
            ByteWidth = (uint)(Marshal.SizeOf<Vertex>() * vertices.Length),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer
        });
    }
    
    private void CreateBlendState()
    {
        var blendDesc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false,
        };
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };
        _blendState = _device!.CreateBlendState(blendDesc);
    }
    
    public void Show()
    {
        if (!_isInitialized || _isVisible) return;
        
        ShowWindow(_hwnd, 8); // SW_SHOWNA - show without activating
        SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, 
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _isVisible = true;
        
        // Start render thread
        _stopRenderThread = false;
        _renderThread = new Thread(RenderLoop) { IsBackground = true };
        _renderThread.Start();
    }
    
    public void Hide()
    {
        if (!_isVisible) return;
        
        _stopRenderThread = true;
        _renderThread?.Join(1000);
        _renderThread = null;
        
        ShowWindow(_hwnd, 0); // SW_HIDE
        _isVisible = false;
    }
    
    private void RenderLoop()
    {
        while (!_stopRenderThread)
        {
            Render();
            Thread.Sleep(16); // ~60 FPS
        }
    }
    
    public void Render()
    {
        if (!_isInitialized || _context == null || _swapChain == null) return;
        
        // Update constant buffer
        var constants = new EdgeLightConstants
        {
            Width = _width,
            Height = _height,
            EdgeThickness = EdgeThickness,
            CornerRadius = CornerRadius,
            InnerCornerRadius = InnerCornerRadius,
            HdrBrightness = HdrBrightness,
            ColorTemperature = ColorTemperature,
            Margin = Margin
        };
        
        var mappedResource = _context.Map(_constantBuffer!, MapMode.WriteDiscard);
        Marshal.StructureToPtr(constants, mappedResource.DataPointer, false);
        _context.Unmap(_constantBuffer!, 0);
        
        // Clear to transparent
        _context.ClearRenderTargetView(_renderTargetView!, new Color4(0, 0, 0, 0));
        
        // Set pipeline
        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetVertexBuffer(0, _vertexBuffer!, (uint)Marshal.SizeOf<Vertex>());
        
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer);
        
        _context.OMSetRenderTargets(_renderTargetView!);
        _context.OMSetBlendState(_blendState);
        _context.RSSetViewport(0, 0, _width, _height);
        
        _context.Draw(6, 0);
        
        _swapChain.Present(1, PresentFlags.None);
    }
    
    public void Resize(int x, int y, int width, int height)
    {
        if (!_isInitialized || width <= 0 || height <= 0) return;
        
        _left = x;
        _top = y;
        _width = width;
        _height = height;
        
        // Move window
        SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
        
        // Resize swap chain
        _renderTargetView?.Dispose();
        _renderTargetView = null;
        
        _swapChain?.ResizeBuffers(2, (uint)width, (uint)height, Format.R16G16B16A16_Float, SwapChainFlags.None);
        
        CreateRenderTargetView();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            // Stop render thread first
            _stopRenderThread = true;
            if (_renderThread != null && _renderThread.IsAlive)
            {
                _renderThread.Join(2000);
            }
            _renderThread = null;
            
            // Hide window
            if (_hwnd != IntPtr.Zero && _isVisible)
            {
                ShowWindow(_hwnd, 0); // SW_HIDE
                _isVisible = false;
            }
            
            // Dispose D3D resources
            _blendState?.Dispose();
            _blendState = null;
            
            _vertexBuffer?.Dispose();
            _vertexBuffer = null;
            
            _constantBuffer?.Dispose();
            _constantBuffer = null;
            
            _inputLayout?.Dispose();
            _inputLayout = null;
            
            _pixelShader?.Dispose();
            _pixelShader = null;
            
            _vertexShader?.Dispose();
            _vertexShader = null;
            
            _renderTargetView?.Dispose();
            _renderTargetView = null;
            
            // Dispose swap chain before composition
            _swapChain?.Dispose();
            _swapChain = null;
            
            // Dispose DirectComposition resources
            _compositionVisual?.Dispose();
            _compositionVisual = null;
            
            _compositionTarget?.Dispose();
            _compositionTarget = null;
            
            _compositionDevice?.Dispose();
            _compositionDevice = null;
            
            // Dispose D3D device last
            _context?.Dispose();
            _context = null;
            
            _device?.Dispose();
            _device = null;
            
            // Destroy window
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        catch
        {
            // Swallow exceptions during dispose to prevent crash on exit
        }
        
        _isInitialized = false;
    }
}
